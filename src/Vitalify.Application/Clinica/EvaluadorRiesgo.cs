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
/// guarda su <see cref="EvaluacionRiesgo.ProximaTomaManualEn"/>. Solo la agrega al repositorio: el llamador confirma con
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
    /// <summary>Evalúa y agrega la evaluación siempre (toma manual o cambio de escala).</summary>
    /// <param name="escala">Escala de SpO2 de la hospitalización (la indica el médico; por defecto la 1).</param>
    public async Task<EvaluacionRiesgo> EvaluarAsync(
        Guid hospitalizacionId, Guid pacienteId, OrigenEvaluacion origen, EscalaSpo2 escala, CancellationToken ct = default)
    {
        var (evaluacion, _) = await CalcularAsync(hospitalizacionId, pacienteId, origen, escala, ct);
        evaluaciones.Agregar(evaluacion);
        return evaluacion;
    }

    /// <summary>
    /// Evalúa con una lectura del wearable y la agrega solo si aporta algo respecto de la última guardada
    /// (<see cref="EvaluacionRiesgo.AportaRespectoDe"/>): un cambio de puntaje, de nivel o de completitud, o 5 min desde
    /// la anterior. Si no aporta, no se agrega y el puntaje queda respaldado por la última guardada.
    /// </summary>
    public async Task<EvaluacionLectura> EvaluarLecturaAsync(
        Guid hospitalizacionId, Guid pacienteId, EscalaSpo2 escala, CancellationToken ct = default)
    {
        var (evaluacion, anterior) = await CalcularAsync(hospitalizacionId, pacienteId, OrigenEvaluacion.Lectura, escala, ct);
        if (anterior is not null && !evaluacion.AportaRespectoDe(anterior))
        {
            return new EvaluacionLectura(evaluacion, false, anterior.Id);
        }

        evaluaciones.Agregar(evaluacion);
        return new EvaluacionLectura(evaluacion, true, evaluacion.Id);
    }

    private async Task<(EvaluacionRiesgo Evaluacion, EvaluacionRiesgo? Anterior)> CalcularAsync(
        Guid hospitalizacionId, Guid pacienteId, OrigenEvaluacion origen, EscalaSpo2 escala, CancellationToken ct)
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

        return (evaluacion, anterior);
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

/// <summary>Una evaluación del wearable y si se guardó.</summary>
/// <param name="RespaldoId">La evaluación guardada con ese puntaje: la nueva, o la última guardada si no aportaba nada.</param>
public sealed record EvaluacionLectura(EvaluacionRiesgo Evaluacion, bool Guardada, Guid RespaldoId);

/// <summary>
/// Motor clínico de la fase 4 enganchado al punto de extensión de la ingesta: recalcula NEWS2 y MEWS con cada lectura
/// que hace avanzar el estado del paciente. Las lecturas atrasadas del búfer (más antiguas que la última) no lo
/// disparan, para que una ráfaga de reenvíos no genere cientos de evaluaciones.
/// <para>
/// Si la evaluación no cambia nada y no pasaron 5 min desde la última guardada, no se guarda (el historial ya tiene ese
/// puntaje), pero <c>RiesgoEvaluado</c> se publica igual: las alertas deben reaccionar a cada evaluación (por ejemplo,
/// al terminar la espera tras atender, AL-E9) y el tablero recibe el puntaje al día.
/// </para>
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

        var evaluada = await evaluador.EvaluarLecturaAsync(lectura.HospitalizacionId, lectura.PacienteId, lectura.EscalaSpo2, ct);
        if (!evaluada.Guardada || (await unidadDeTrabajo.GuardarCambiosAsync(ct)).EsExito)
        {
            await publicador.PublicarAsync(evaluada.Evaluacion, evaluada.RespaldoId, ct);
        }
    }
}
