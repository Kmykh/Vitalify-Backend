using Vitalify.Application.Alertas;
using Vitalify.Application.Puertos;
using Vitalify.Application.Telemetria;
using Vitalify.Domain.Clinica;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Application.Clinica;

/// <summary>
/// Arma los parámetros de NEWS2/MEWS de una hospitalización y calcula la evaluación. Por cada parámetro toma el valor
/// más reciente entre:
/// <list type="bullet">
/// <item>el del wearable, si está vigente (no más antiguo que la vigencia de la telemetría, 90 s por defecto);</item>
/// <item>el de la última toma manual que todavía cuenta: vale hasta su próxima toma más la tolerancia (con un tope de
/// 12 h), y si una evaluación posterior acortó el plazo porque el paciente empeoró, hasta ese plazo.</item>
/// </list>
/// Lo que no está en ninguna de las dos fuentes queda como faltante y el puntaje se marca parcial. Cada evaluación
/// guarda su <see cref="EvaluacionRiesgo.ProximaTomaManualEn"/>. No guarda: el llamador confirma con
/// <see cref="IUnidadDeTrabajoHistorial"/> y publica <c>RiesgoEvaluado</c>.
/// </summary>
public sealed class EvaluadorRiesgo(
    IRepositorioEstadoSignos estados,
    IRepositorioObservaciones observaciones,
    IRepositorioEvaluaciones evaluaciones,
    OpcionesTelemetria opcionesTelemetria,
    OpcionesClinicas opcionesClinicas,
    IReloj reloj)
{
    /// <param name="escala">Escala de SpO2 de la hospitalización (la indica el médico; por defecto la 1).</param>
    public async Task<EvaluacionRiesgo> EvaluarAsync(
        Guid hospitalizacionId, Guid pacienteId, OrigenEvaluacion origen, EscalaSpo2 escala, CancellationToken ct = default)
    {
        var ahora = reloj.AhoraUtc;
        var estado = await estados.ObtenerAsync(hospitalizacionId, ct);
        var anterior = await evaluaciones.ObtenerUltimaAsync(hospitalizacionId, ct);
        var tomas = await observaciones.ListarDesdeAsync(
            hospitalizacionId, ahora - FrecuenciaTomaManual.TopeVigencia - opcionesClinicas.ToleranciaToma, ct);
        var vigentes = tomas.Where(o => FrecuenciaTomaManual.Cuenta(o, ahora, anterior, opcionesClinicas.ToleranciaToma)).ToList();

        var evaluacion = EvaluacionRiesgo.Calcular(hospitalizacionId, pacienteId, ahora, origen, Combinar(estado, vigentes, ahora), escala);

        // El plazo sale de la última toma (aunque ya no cuente) y del NEWS2 de ahora: si empeora, se acorta. Sin ninguna
        // toma, el plazo queda fijo desde la primera evaluación (si avanzara con cada lectura, nunca vencería).
        var ultimaToma = tomas.FirstOrDefault()?.ObservadaEn ?? (await observaciones.ObtenerUltimaTomaAsync(hospitalizacionId, ct))?.ObservadaEn;
        evaluacion.FijarProximaTomaManual(ultimaToma is { } toma
            ? FrecuenciaTomaManual.Proxima(toma, ahora, evaluacion.News2())
            : anterior?.ProximaTomaManualEn ?? ahora);

        evaluaciones.Agregar(evaluacion);
        return evaluacion;
    }

    private ParametrosClinicos Combinar(EstadoSignosActual? e, IReadOnlyList<ObservacionEnfermeria> manuales, DateTime ahora)
    {
        (T? Valor, DateTime? En) Sensor<T>(T? valor, DateTime? medidoEn)
            where T : struct =>
            valor is not null && Vigencia.EstadoDe(medidoEn, ahora, opcionesTelemetria.Vigencia) == EstadoVariable.Vigente
                ? (valor, medidoEn)
                : (null, null);

        (T? Valor, DateTime? En) Manual<T>(Func<ObservacionEnfermeria, T?> campo)
            where T : struct =>
            manuales.FirstOrDefault(o => campo(o) is not null) is { } o ? (campo(o), o.ObservadaEn) : (null, null);

        static T? MasReciente<T>((T? Valor, DateTime? En) a, (T? Valor, DateTime? En) b)
            where T : struct =>
            a.Valor is null ? b.Valor : b.Valor is null ? a.Valor : (a.En >= b.En ? a.Valor : b.Valor);

        var temperaturaSensor = Sensor(e?.TempValor, e?.TempMedidoEn);
        if (temperaturaSensor.Valor is { } t)
        {
            temperaturaSensor = (t + opcionesClinicas.AjusteTemperaturaSensor, temperaturaSensor.En);
        }

        return new ParametrosClinicos(
            Fr: MasReciente(Sensor(e?.FrValor, e?.FrMedidoEn), Manual(o => o.Fr)),
            Spo2: Sensor(e?.Spo2Valor, e?.Spo2MedidoEn).Valor,
            OxigenoSuplementario: Manual(o => o.OxigenoSuplementario).Valor,
            Pas: MasReciente(Sensor(e?.PasValor, e?.PasMedidoEn), Manual(o => o.Pas)),
            Fc: Sensor(e?.FcValor, e?.FcMedidoEn).Valor,
            Conciencia: Manual(o => o.Conciencia).Valor,
            Temperatura: MasReciente(temperaturaSensor, Manual(o => o.Temperatura)));
    }
}

/// <summary>
/// Motor clínico de la fase 4 enganchado al punto de extensión de la ingesta: recalcula NEWS2 y MEWS con cada lectura
/// que hace avanzar el estado del paciente. Las lecturas atrasadas del búfer (más antiguas que la última) no lo
/// disparan, para que una ráfaga de reenvíos no genere cientos de evaluaciones.
/// </summary>
public sealed class EvaluarRiesgoAlRegistrarLectura(
    EvaluadorRiesgo evaluador, IRepositorioEstadoSignos estados, IUnidadDeTrabajoHistorial unidadDeTrabajo, PublicadorRiesgoEvaluado publicador)
    : IManejadorLecturaRegistrada
{
    public async Task ManejarAsync(LecturaRegistrada lectura, CancellationToken ct = default)
    {
        var estado = await estados.ObtenerAsync(lectura.HospitalizacionId, ct);
        if (estado?.UltimaLecturaEn is { } ultima && lectura.MedidoEn < ultima)
        {
            return;
        }

        var evaluacion = await evaluador.EvaluarAsync(lectura.HospitalizacionId, lectura.PacienteId, OrigenEvaluacion.Lectura, lectura.EscalaSpo2, ct);
        if ((await unidadDeTrabajo.GuardarCambiosAsync(ct)).EsExito)
        {
            await publicador.PublicarAsync(evaluacion, ct);
        }
    }
}
