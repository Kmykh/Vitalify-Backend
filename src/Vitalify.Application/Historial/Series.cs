using Vitalify.Application.Clinica;
using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Clinica;
using Vitalify.Domain.Historial;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Application.Historial;

internal static class Agrupacion
{
    /// <summary>El intervalo de <paramref name="codigo"/> (null si no se pidió agrupar), o el error de validación.</summary>
    public static Resultado<IntervaloAgrupacion?> Leer(string? codigo, IReadOnlyList<IntervaloAgrupacion> permitidos)
    {
        if (string.IsNullOrWhiteSpace(codigo))
        {
            return Resultado<IntervaloAgrupacion?>.Exito(null);
        }

        return IntervaloAgrupacion.Buscar(codigo, permitidos) is { } intervalo
            ? Resultado<IntervaloAgrupacion?>.Exito(intervalo)
            : ErroresComunes.ValidacionDeCampo("agrupar", $"agrupar debe ser {string.Join(", ", permitidos.Select(p => p.Codigo))}.");
    }
}

/// <param name="Agrupar"><c>1m</c>, <c>5m</c>, <c>15m</c> o <c>1h</c>; sin él, cada lectura.</param>
public sealed record ObtenerHistorialSignosConsulta(
    Guid PacienteId, DateTime? Desde = null, DateTime? Hasta = null, Guid? HospitalizacionId = null, string? Agrupar = null,
    Guid UsuarioId = default, string? Ip = null);

/// <summary>
/// Una lectura del wearable tal como se guardó (solo las variables válidas). <c>Seq</c> es el contador del firmware y
/// <c>RecibidoEn</c>, cuándo la registró el backend (con <c>MedidoEn</c> da el retraso de la transmisión).
/// </summary>
public sealed record LecturaHistorialDto(
    DateTime MedidoEn, DateTime RecibidoEn, long Seq, int? Fc, int? Spo2, decimal? Temperatura, int? Fr, int? Pas, int? Pad,
    bool Caida, string Origen)
{
    public static LecturaHistorialDto Desde(LecturaSignos l) =>
        new(l.MedidoEn, l.RecibidoEn, l.Seq, l.Fc, l.Spo2, l.Temperatura, l.Fr, l.Pas, l.Pad, l.Caida, l.Origen.ToString());
}

/// <summary>Lecturas de un intervalo: promedio, mínimo y máximo por variable (null si no se midió) y si hubo una caída.</summary>
/// <param name="Resumido">Sale del resumen de 5 min de un episodio cerrado: con <c>agrupar=1m</c>, el grupo dura 5 min.</param>
public sealed record GrupoSignosDto(
    DateTime Inicio, DateTime Fin, int Lecturas, EstadisticaSerie? Fc, EstadisticaSerie? Spo2, EstadisticaSerie? Temperatura,
    EstadisticaSerie? Fr, EstadisticaSerie? Pas, EstadisticaSerie? Pad, bool Caida, bool Resumido = false)
{
    public static GrupoSignosDto Desde(GrupoLecturas g, IntervaloAgrupacion intervalo)
    {
        var duracion = g.Resumido && intervalo.Duracion < PoliticaRetencion.IntervaloResumen ? PoliticaRetencion.IntervaloResumen : intervalo.Duracion;
        return new(g.Inicio, g.Inicio + duracion, g.Lecturas, g.Fc, g.Spo2, g.Temperatura, g.Fr, g.Pas, g.Pad, g.Caida, g.Resumido);
    }
}

/// <summary>Historial de signos: <see cref="Lecturas"/> sin agrupar, o <see cref="Grupos"/> (y lecturas vacía) si se agrupó.</summary>
/// <param name="ResumidoHasta">
/// Si el episodio ya pasó por la retención, hasta dónde las lecturas crudas se reemplazaron por resúmenes de 5 min: sin
/// <c>agrupar</c> no aparecen (usa <c>agrupar</c>). Null si no se resumió nada.
/// </param>
public sealed record HistorialSignosDto(
    Guid PacienteId, Guid HospitalizacionId, DateTime Desde, DateTime Hasta, IReadOnlyList<LecturaHistorialDto> Lecturas,
    string? Agrupar = null, IReadOnlyList<GrupoSignosDto>? Grupos = null, DateTime? ResumidoHasta = null);

/// <summary>
/// Lecturas del wearable de un episodio (HU19): por defecto el episodio activo o el último, completo. Sin agrupar, como
/// máximo 7 días; agrupadas, el episodio completo. Solo lee el historial: no recalcula estados ni puntajes. Si el
/// episodio ya pasó por la retención, lo agrupado sale del resumen de 5 min y <see cref="HistorialSignosDto.ResumidoHasta"/>
/// lo indica.
/// </summary>
public sealed class ObtenerHistorialSignos(
    EpisodiosPaciente episodios, IRepositorioLecturas lecturas, AuditoriaHistorial auditoria, IReloj reloj)
{
    public async Task<Resultado<HistorialSignosDto>> EjecutarAsync(ObtenerHistorialSignosConsulta consulta, CancellationToken ct = default)
    {
        var intervalo = Agrupacion.Leer(consulta.Agrupar, IntervaloAgrupacion.ParaSignos);
        if (!intervalo.EsExito)
        {
            return intervalo.Error;
        }

        var elegido = await episodios.ElegirAsync(consulta.PacienteId, consulta.HospitalizacionId, ct);
        if (!elegido.EsExito)
        {
            return elegido.Error;
        }

        var hospitalizacion = elegido.Valor.Hospitalizacion;
        var ventana = EpisodiosPaciente.Ventana(hospitalizacion, reloj.AhoraUtc, consulta.Desde, consulta.Hasta, intervalo.Valor is not null);
        if (!ventana.EsExito)
        {
            return ventana.Error;
        }

        var (desde, hasta) = (ventana.Valor.Desde, ventana.Valor.Hasta);
        var resumidoHasta = hospitalizacion.EstaActiva ? null : await lecturas.ObtenerResumidoHastaAsync(hospitalizacion.Id, ct);
        HistorialSignosDto dto;
        if (intervalo.Valor is { } agrupar)
        {
            var grupos = await lecturas.AgruparAsync(hospitalizacion.Id, desde, hasta, agrupar.Segundos, ct);
            dto = new HistorialSignosDto(consulta.PacienteId, hospitalizacion.Id, desde, hasta, [], agrupar.Codigo,
                grupos.Select(g => GrupoSignosDto.Desde(g, agrupar)).ToList(), resumidoHasta);
        }
        else
        {
            var filas = await lecturas.ListarPorHospitalizacionAsync(hospitalizacion.Id, desde, hasta, ct);
            dto = new HistorialSignosDto(
                consulta.PacienteId, hospitalizacion.Id, desde, hasta, filas.Select(LecturaHistorialDto.Desde).ToList(), ResumidoHasta: resumidoHasta);
        }

        var auditado = await auditoria.RegistrarAsync(consulta.PacienteId, hospitalizacion.Id, "signos/historial", consulta.UsuarioId, consulta.Ip, ct);
        return auditado.EsExito ? dto : auditado.Error;
    }
}

/// <param name="Agrupar"><c>5m</c>, <c>15m</c> o <c>1h</c>; sin él, cada evaluación.</param>
public sealed record ObtenerHistorialRiesgoConsulta(
    Guid PacienteId, Guid? HospitalizacionId, DateTime? Desde, DateTime? Hasta, string? Agrupar, Guid UsuarioId, string? Ip);

/// <summary>Una evaluación del historial: totales, nivel, si fue completa, puntos por parámetro, faltantes y valores usados.</summary>
public sealed record EvaluacionHistorialDto(DateTime EvaluadaEn, string Origen, PuntajeDto News2, PuntajeDto Mews, ParametrosUsadosDto Parametros)
{
    public static EvaluacionHistorialDto Desde(EvaluacionRiesgo e) =>
        new(e.EvaluadaEn, e.Origen.ToString(), PuntajeDto.De(e.News2()), PuntajeDto.De(e.Mews()), ParametrosUsadosDto.De(e));
}

/// <summary>Un puntaje en un intervalo: mínimo, máximo, último y el peor nivel.</summary>
public sealed record PuntajeAgrupadoDto(int Minimo, int Maximo, int Ultimo, string NivelMaximo);

/// <param name="Completas">Cuántas evaluaciones del intervalo tuvieron NEWS2 y MEWS completos.</param>
public sealed record GrupoRiesgoDto(DateTime Inicio, DateTime Fin, int Evaluaciones, int Completas, PuntajeAgrupadoDto News2, PuntajeAgrupadoDto Mews)
{
    public static GrupoRiesgoDto Desde(GrupoEvaluaciones g, IntervaloAgrupacion intervalo) =>
        new(g.Inicio, g.Inicio + intervalo.Duracion, g.Evaluaciones, g.Completas,
            new PuntajeAgrupadoDto(g.News2Minimo, g.News2Maximo, g.News2Ultimo, TextosClinicos.De(g.News2NivelMaximo)),
            new PuntajeAgrupadoDto(g.MewsMinimo, g.MewsMaximo, g.MewsUltimo, TextosClinicos.De(g.MewsNivelMaximo)));
}

/// <summary>Historial de riesgo: <see cref="Evaluaciones"/> sin agrupar, o <see cref="Grupos"/> (y evaluaciones vacía) si se agrupó.</summary>
public sealed record HistorialRiesgoDto(
    Guid PacienteId, Guid HospitalizacionId, DateTime Desde, DateTime Hasta, string? Agrupar,
    IReadOnlyList<EvaluacionHistorialDto> Evaluaciones, IReadOnlyList<GrupoRiesgoDto>? Grupos);

/// <summary>
/// Evaluaciones NEWS2/MEWS de un episodio, con el mismo rango y los mismos límites que el historial de signos. El
/// desglose por parámetro se recalcula al leer, como en <c>GET /riesgo</c>.
/// </summary>
public sealed class ObtenerHistorialRiesgo(
    EpisodiosPaciente episodios, IRepositorioEvaluaciones evaluaciones, AuditoriaHistorial auditoria, IReloj reloj)
{
    public async Task<Resultado<HistorialRiesgoDto>> EjecutarAsync(ObtenerHistorialRiesgoConsulta consulta, CancellationToken ct = default)
    {
        var intervalo = Agrupacion.Leer(consulta.Agrupar, IntervaloAgrupacion.ParaRiesgo);
        if (!intervalo.EsExito)
        {
            return intervalo.Error;
        }

        var elegido = await episodios.ElegirAsync(consulta.PacienteId, consulta.HospitalizacionId, ct);
        if (!elegido.EsExito)
        {
            return elegido.Error;
        }

        var hospitalizacion = elegido.Valor.Hospitalizacion;
        var ventana = EpisodiosPaciente.Ventana(hospitalizacion, reloj.AhoraUtc, consulta.Desde, consulta.Hasta, intervalo.Valor is not null);
        if (!ventana.EsExito)
        {
            return ventana.Error;
        }

        var (desde, hasta) = (ventana.Valor.Desde, ventana.Valor.Hasta);
        HistorialRiesgoDto dto;
        if (intervalo.Valor is { } agrupar)
        {
            var grupos = await evaluaciones.AgruparAsync(hospitalizacion.Id, desde, hasta, agrupar.Segundos, ct);
            dto = new HistorialRiesgoDto(consulta.PacienteId, hospitalizacion.Id, desde, hasta, agrupar.Codigo, [],
                grupos.Select(g => GrupoRiesgoDto.Desde(g, agrupar)).ToList());
        }
        else
        {
            var filas = await evaluaciones.ListarAsync(hospitalizacion.Id, desde, hasta, ct);
            dto = new HistorialRiesgoDto(consulta.PacienteId, hospitalizacion.Id, desde, hasta, null, filas.Select(EvaluacionHistorialDto.Desde).ToList(), null);
        }

        var auditado = await auditoria.RegistrarAsync(consulta.PacienteId, hospitalizacion.Id, "riesgo/historial", consulta.UsuarioId, consulta.Ip, ct);
        return auditado.EsExito ? dto : auditado.Error;
    }
}
