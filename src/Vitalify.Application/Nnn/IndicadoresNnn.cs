using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Application.Turnos;
using Vitalify.Domain.Nnn;
using Vitalify.Domain.Turnos;

namespace Vitalify.Application.Nnn;

/// <param name="TasaAceptacion">Confirmadas / (confirmadas + descartadas): de las que la enfermera decidió, cuántas aceptó.</param>
public sealed record ReglaIndicadorDto(string Regla, int Emitidas, int Confirmadas, int Descartadas, int Expiradas, int Pendientes, double? TasaAceptacion);

/// <param name="TasaAceptacion">Confirmadas / (confirmadas + descartadas).</param>
/// <param name="TasaAceptacionSobreEmitidas">Confirmadas / emitidas (incluye las que expiraron o siguen pendientes).</param>
public sealed record SugerenciasIndicadorDto(
    int Emitidas, int Confirmadas, int Descartadas, int Expiradas, int Pendientes, double? TasaAceptacion, double? TasaAceptacionSobreEmitidas,
    IReadOnlyList<ReglaIndicadorDto> PorRegla);

/// <summary>Desde que se creó una alerta hasta la primera intervención NIC registrada por ella.</summary>
public sealed record TiempoRespuestaIndicadorDto(int Alertas, double? MinutosPromedio, double? MinutosMediana);

/// <param name="Porcentaje">Diagnósticos con al menos una valoración NOC en cada guardia en que estuvieron activos (6 h o más).</param>
public sealed record ValoracionPorTurnoIndicadorDto(int Diagnosticos, int DiagnosticosConValoracionEnCadaTurno, double? Porcentaje, int TurnosEvaluados, int TurnosConValoracion);

/// <param name="CambioMedio">Promedio de (última valoración antes del cierre − puntuación basal) de los resultados de diagnósticos resueltos.</param>
public sealed record CambioNocIndicadorDto(int ResultadosDeDiagnosticosResueltos, int ResultadosConValoracion, double? CambioMedio);

/// <summary>
/// Automatización asistida (fase 9): cuántos paquetes se confirmaron tal cual (sesgo de automatización: un "sí" sin revisar
/// no es una decisión), cuánto tardó la enfermera en decidir, cuántas ejecuciones registró sola una toma manual completa y en
/// cuántas guardias con plan se firmó la nota del PAE.
/// </summary>
public sealed record AutomatizacionIndicadorDto(
    int PaquetesConfirmados, int ConfirmadosSinCambios, double? TasaSinCambios, double? MinutosHastaConfirmarMediana,
    int EjecucionesPorTomaManual, int GuardiasConPlan, int GuardiasConNotaTurno, double? PorcentajeNotaTurno);

/// <summary>Indicadores agregados del proceso de enfermería para el capítulo de validación. Nunca datos de pacientes.</summary>
public sealed record IndicadoresNnnDto(
    DateTime Desde, DateTime Hasta, string VersionReglas, SugerenciasIndicadorDto Sugerencias, TiempoRespuestaIndicadorDto AlertaAPrimeraIntervencion,
    ValoracionPorTurnoIndicadorDto ValoracionNocPorTurno, CambioNocIndicadorDto CambioNoc, AutomatizacionIndicadorDto? Automatizacion = null);

public sealed record ObtenerIndicadoresNnnConsulta(DateTime? Desde, DateTime? Hasta);

/// <summary>Agregados del proceso de enfermería NNN en un rango (por defecto, los últimos 30 días). Solo cantidades y promedios.</summary>
public sealed class ObtenerIndicadoresNnn(IRepositorioPlanCuidados plan, IRepositorioNotas notas, CatalogoTurnos catalogo, IReloj reloj)
{
    public static readonly TimeSpan RangoPorDefecto = TimeSpan.FromDays(30);
    public static readonly TimeSpan RangoMaximo = TimeSpan.FromDays(366);

    /// <summary>Una guardia cuenta para un diagnóstico si estuvo activo al menos la mitad de ella.</summary>
    public static readonly TimeSpan MinimoActivoEnGuardia = TimeSpan.FromHours(6);

    public async Task<Resultado<IndicadoresNnnDto>> EjecutarAsync(ObtenerIndicadoresNnnConsulta consulta, CancellationToken ct = default)
    {
        var ahora = reloj.AhoraUtc;
        var hasta = consulta.Hasta?.ToUniversalTime() ?? ahora;
        var desde = consulta.Desde?.ToUniversalTime() ?? hasta - RangoPorDefecto;
        if (desde >= hasta)
        {
            return ErroresComunes.ValidacionDeCampo("desde", "desde debe ser anterior a hasta.");
        }

        if (hasta - desde > RangoMaximo)
        {
            return ErroresComunes.ValidacionDeCampo("desde", "El rango puede ser de hasta 366 días.");
        }

        var emitidas = await plan.ListarSugerenciasEmitidasEntreAsync(desde, hasta, ct);
        var tiempos = await plan.ListarTiemposAlertaIntervencionAsync(desde, hasta, ct);
        var diagnosticos = await plan.ListarConfirmadosActivosEntreAsync(desde, hasta, ct);

        var conNota = await notas.ListarGuardiasConNotaTurnoAsync(desde, hasta, ct);
        return new IndicadoresNnnDto(
            desde, hasta, MotorSugerenciasNnn.VersionReglas, Sugerencias(emitidas), Tiempos(tiempos),
            ValoracionPorTurno(diagnosticos, desde, hasta < ahora ? hasta : ahora), CambioNoc(diagnosticos, desde, hasta),
            Automatizacion(emitidas, diagnosticos, conNota, desde, hasta < ahora ? hasta : ahora));
    }

    private AutomatizacionIndicadorDto Automatizacion(
        IReadOnlyList<SugerenciaEmitida> emitidas, PlanCuidadosLectura l, IReadOnlyList<(Guid HospitalizacionId, string Guardia)> conNota,
        DateTime desde, DateTime hasta)
    {
        var confirmados = l.Diagnosticos.Where(d => d.ConfirmadoSinCambios is not null && d.ConfirmadoEn >= desde && d.ConfirmadoEn < hasta).ToList();
        var sinCambios = confirmados.Count(d => d.ConfirmadoSinCambios == true);
        var minutos = emitidas.Where(s => s.Estado == EstadoSugerencia.Confirmada && s.ResueltaEn is not null)
            .Select(s => (s.ResueltaEn!.Value - s.EmitidaEn).TotalMinutes).Where(m => m >= 0).OrderBy(m => m).ToList();
        double? mediana = minutos.Count == 0 ? null
            : Math.Round(minutos.Count % 2 == 1 ? minutos[minutos.Count / 2] : (minutos[(minutos.Count / 2) - 1] + minutos[minutos.Count / 2]) / 2, 2);
        var porToma = l.Ejecuciones.Count(e => e.Origen == OrigenEjecucion.TomaManual && e.EjecutadaEn >= desde && e.EjecutadaEn < hasta);

        // Guardias con plan: las de cada hospitalización con algún diagnóstico activo 6 h o más en ellas.
        var guardiasConPlan = new HashSet<(Guid, string)>();
        foreach (var d in l.Diagnosticos.Where(d => d.ConfirmadoEn is not null))
        {
            var inicio = d.ConfirmadoEn!.Value > desde ? d.ConfirmadoEn.Value : desde;
            var fin = d.CerradoEn is { } c && c < hasta ? c : hasta;
            foreach (var (clave, intervalo) in GuardiasConClaveEntre(inicio, fin))
            {
                if (Solape(intervalo, inicio, fin) >= MinimoActivoEnGuardia)
                {
                    guardiasConPlan.Add((d.HospitalizacionId, clave));
                }
            }
        }

        var firmadas = guardiasConPlan.Count(g => conNota.Contains(g));
        return new AutomatizacionIndicadorDto(
            confirmados.Count, sinCambios, confirmados.Count == 0 ? null : Math.Round(100.0 * sinCambios / confirmados.Count, 2), mediana,
            porToma, guardiasConPlan.Count, firmadas, guardiasConPlan.Count == 0 ? null : Math.Round(100.0 * firmadas / guardiasConPlan.Count, 2));
    }

    private IEnumerable<(string Clave, IntervaloTurno Intervalo)> GuardiasConClaveEntre(DateTime inicio, DateTime fin)
    {
        for (var fecha = catalogo.FechaLocal(inicio).AddDays(-1); fecha <= catalogo.FechaLocal(fin); fecha = fecha.AddDays(1))
        {
            foreach (var tipo in new[] { TipoTurno.GuardiaDiurna, TipoTurno.GuardiaNocturna })
            {
                var turno = new TurnoDelDia(fecha, tipo);
                var g = catalogo.Intervalo(turno);
                if (g.InicioUtc < fin && inicio < g.FinUtc)
                {
                    yield return (Guardias.Clave(turno)!, g);
                }
            }
        }
    }

    private static SugerenciasIndicadorDto Sugerencias(IReadOnlyList<SugerenciaEmitida> s)
    {
        static (int E, int C, int D, int X, int P) Contar(IEnumerable<SugerenciaEmitida> l)
        {
            var lista = l.ToList();
            return (lista.Count, lista.Count(x => x.Estado == EstadoSugerencia.Confirmada), lista.Count(x => x.Estado == EstadoSugerencia.Descartada),
                lista.Count(x => x.Estado == EstadoSugerencia.Expirada), lista.Count(x => x.Estado == EstadoSugerencia.Pendiente));
        }

        static double? Tasa(int numerador, int denominador) => denominador == 0 ? null : Math.Round((double)numerador / denominador, 4);

        var total = Contar(s);
        var porRegla = s.GroupBy(x => x.Regla)
            .OrderBy(g => g.Key.Length).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var t = Contar(g);
                return new ReglaIndicadorDto(g.Key, t.E, t.C, t.D, t.X, t.P, Tasa(t.C, t.C + t.D));
            })
            .ToList();
        return new SugerenciasIndicadorDto(total.E, total.C, total.D, total.X, total.P, Tasa(total.C, total.C + total.D), Tasa(total.C, total.E), porRegla);
    }

    private static TiempoRespuestaIndicadorDto Tiempos(IReadOnlyList<TiempoAlertaIntervencion> tiempos)
    {
        var minutos = tiempos.Select(t => (t.PrimeraIntervencionEn - t.AlertaCreadaEn).TotalMinutes).Where(m => m >= 0).OrderBy(m => m).ToList();
        if (minutos.Count == 0)
        {
            return new TiempoRespuestaIndicadorDto(0, null, null);
        }

        var mediana = minutos.Count % 2 == 1 ? minutos[minutos.Count / 2] : (minutos[(minutos.Count / 2) - 1] + minutos[minutos.Count / 2]) / 2;
        return new TiempoRespuestaIndicadorDto(minutos.Count, Math.Round(minutos.Average(), 2), Math.Round(mediana, 2));
    }

    private ValoracionPorTurnoIndicadorDto ValoracionPorTurno(PlanCuidadosLectura l, DateTime desde, DateTime hasta)
    {
        int diagnosticos = 0, completos = 0, turnos = 0, turnosValorados = 0;
        foreach (var d in l.Diagnosticos.Where(d => d.ConfirmadoEn is not null))
        {
            var inicio = d.ConfirmadoEn!.Value > desde ? d.ConfirmadoEn.Value : desde;
            var fin = d.CerradoEn is { } c && c < hasta ? c : hasta;
            var valoradas = l.ResultadosDe(d.Id).SelectMany(r => l.ValoracionesDe(r.Id)).Select(v => v.ValoradaEn).ToList();
            var guardias = GuardiasEntre(inicio, fin)
                .Where(g => Solape(g, inicio, fin) >= MinimoActivoEnGuardia)
                .ToList();
            if (guardias.Count == 0)
            {
                continue;
            }

            var conValoracion = guardias.Count(g => valoradas.Any(g.Contiene));
            diagnosticos++;
            turnos += guardias.Count;
            turnosValorados += conValoracion;
            if (conValoracion == guardias.Count)
            {
                completos++;
            }
        }

        return new ValoracionPorTurnoIndicadorDto(
            diagnosticos, completos, diagnosticos == 0 ? null : Math.Round(100.0 * completos / diagnosticos, 2), turnos, turnosValorados);
    }

    private static CambioNocIndicadorDto CambioNoc(PlanCuidadosLectura l, DateTime desde, DateTime hasta)
    {
        var resueltos = l.Diagnosticos.Where(d => d.Estado == EstadoDiagnosticoPlan.Resuelto && d.CerradoEn >= desde && d.CerradoEn < hasta).ToList();
        var cambios = new List<double>();
        var resultados = 0;
        foreach (var d in resueltos)
        {
            foreach (var r in l.ResultadosDe(d.Id))
            {
                resultados++;
                var antesDelCierre = l.ValoracionesDe(r.Id).Where(v => v.ValoradaEn <= d.CerradoEn).ToList();
                if (PresentadorPlanCuidados.Ultima(antesDelCierre) is { } ultima)
                {
                    cambios.Add(ultima.Puntuacion - r.PuntuacionBasal);
                }
            }
        }

        return new CambioNocIndicadorDto(resultados, cambios.Count, cambios.Count == 0 ? null : Math.Round(cambios.Average(), 2));
    }

    private IEnumerable<IntervaloTurno> GuardiasEntre(DateTime inicio, DateTime fin)
    {
        for (var fecha = catalogo.FechaLocal(inicio).AddDays(-1); fecha <= catalogo.FechaLocal(fin); fecha = fecha.AddDays(1))
        {
            foreach (var tipo in new[] { TipoTurno.GuardiaDiurna, TipoTurno.GuardiaNocturna })
            {
                var g = catalogo.Intervalo(new TurnoDelDia(fecha, tipo));
                if (g.InicioUtc < fin && inicio < g.FinUtc)
                {
                    yield return g;
                }
            }
        }
    }

    private static TimeSpan Solape(IntervaloTurno g, DateTime inicio, DateTime fin) =>
        (g.FinUtc < fin ? g.FinUtc : fin) - (g.InicioUtc > inicio ? g.InicioUtc : inicio);
}
