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
/// guarda su <see cref="EvaluacionRiesgo.ProximaTomaManualEn"/> y de dónde salió cada valor. Solo la agrega al
/// repositorio: el llamador confirma con <see cref="IUnidadDeTrabajoHistorial"/> y publica <c>RiesgoEvaluado</c>.
/// <para>
/// Precisión del wearable (fase 9, <see cref="PrecisionSensor"/>): la FC, la SpO2 y la temperatura del sensor se puntúan con
/// la mediana de los últimos 60 s y la FR con la de sus 3 últimos valores (vigente 5 min); la temperatura de piel de
/// 36,0 °C o menos no puntúa (queda faltante: termómetro).
/// </para>
/// </summary>
public sealed class EvaluadorRiesgo(
    IRepositorioLecturas lecturas,
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

        // Hasta un par de minutos "en el futuro": el reloj del wearable puede ir algo adelantado (la ingesta lo admite).
        var recientes = await lecturas.ListarPorHospitalizacionAsync(
            hospitalizacionId, ahora - PrecisionSensor.VigenciaFr, ahora + PrecisionSensor.VentanaMediana * 2, ct);
        var (parametros, fuentes) = Combinar(estado, recientes, vigentes, ahora);
        var evaluacion = EvaluacionRiesgo.Calcular(hospitalizacionId, pacienteId, ahora, origen, parametros, escala, fuentes);

        // El plazo sale de la última toma (aunque ya no cuente) y del NEWS2 de ahora: si empeora, se acorta. Sin ninguna
        // toma, el plazo queda fijo desde la primera evaluación (si avanzara con cada lectura, nunca vencería).
        var ultimaToma = tomas.FirstOrDefault()?.ObservadaEn ?? (await observaciones.ObtenerUltimaTomaAsync(hospitalizacionId, ct))?.ObservadaEn;
        evaluacion.FijarProximaTomaManual(ultimaToma is { } toma
            ? FrecuenciaTomaManual.Proxima(toma, ahora, evaluacion.News2())
            : anterior?.ProximaTomaManualEn ?? ahora);

        return (evaluacion, anterior);
    }

    private (ParametrosClinicos Parametros, FuentesSignos Fuentes) Combinar(
        EstadoSignosActual? e, IReadOnlyList<LecturaSignos> recientes, IReadOnlyList<ObservacionEnfermeria> manuales, DateTime ahora)
    {
        // El valor del sensor solo cuenta si está vigente; se puntúa la mediana de la ventana (sin lecturas en ella, el último).
        (T? Valor, DateTime? En) Sensor<T>(T? valor, DateTime? medidoEn, TimeSpan vigencia, Func<IEnumerable<LecturaSignos>, T?> mediana)
            where T : struct =>
            valor is not null && Vigencia.EstadoDe(medidoEn, ahora, vigencia) == EstadoVariable.Vigente
                ? (mediana(recientes) ?? valor, medidoEn)
                : (null, null);

        // La ventana de la mediana se ancla en la lectura más reciente (no en el reloj del servidor).
        var ancla = recientes.Count > 0 ? recientes.Max(l => l.MedidoEn) : ahora;
        IEnumerable<LecturaSignos> Ventana(IEnumerable<LecturaSignos> l) => l.Where(x => x.MedidoEn >= ancla - PrecisionSensor.VentanaMediana);

        (T? Valor, DateTime? En) Manual<T>(Func<ObservacionEnfermeria, T?> campo)
            where T : struct =>
            manuales.FirstOrDefault(o => campo(o) is not null) is { } o ? (campo(o), o.ObservadaEn) : (null, null);

        static (T? Valor, FuenteSigno? Fuente) MasReciente<T>((T? Valor, DateTime? En) sensor, (T? Valor, DateTime? En) manual)
            where T : struct =>
            sensor.Valor is null ? (manual.Valor, manual.Valor is null ? null : FuenteSigno.Manual)
            : manual.Valor is null || sensor.En >= manual.En ? (sensor.Valor, FuenteSigno.Sensor)
            : (manual.Valor, FuenteSigno.Manual);

        var temperaturaSensor = Sensor(e?.TempValor, e?.TempMedidoEn, opcionesTelemetria.Vigencia,
            l => PrecisionSensor.Mediana(Ventana(l).Select(x => x.Temperatura)));
        if (temperaturaSensor.Valor is { } t)
        {
            // Temperatura de piel corregida; la de 36,0 °C o menos no puntúa (no es temperatura central).
            var corregida = t + opcionesClinicas.AjusteTemperaturaSensor;
            temperaturaSensor = PrecisionSensor.TemperaturaPielPuntua(corregida) ? (corregida, temperaturaSensor.En) : (null, null);
        }

        // La FR del sensor solo se mide quieto y ya trae su propia ventana: vale 5 min y se toma la mediana de sus 3 últimos valores.
        var frSensor = Sensor(e?.FrValor, e?.FrMedidoEn, PrecisionSensor.VigenciaFr,
            l => PrecisionSensor.Mediana(l.Where(x => x.Fr is not null).OrderByDescending(x => x.MedidoEn).Take(PrecisionSensor.LecturasFr).Select(x => x.Fr)));
        var fr = MasReciente(frSensor, Manual(o => o.Fr));
        var pas = MasReciente(Sensor(e?.PasValor, e?.PasMedidoEn, opcionesTelemetria.Vigencia, _ => null), Manual(o => o.Pas));
        var temperatura = MasReciente(temperaturaSensor, Manual(o => o.Temperatura));

        var parametros = new ParametrosClinicos(
            Fr: fr.Valor,
            Spo2: Sensor(e?.Spo2Valor, e?.Spo2MedidoEn, opcionesTelemetria.Vigencia, l => PrecisionSensor.Mediana(Ventana(l).Select(x => x.Spo2))).Valor,
            OxigenoSuplementario: Manual(o => o.OxigenoSuplementario).Valor,
            Pas: pas.Valor,
            Fc: Sensor(e?.FcValor, e?.FcMedidoEn, opcionesTelemetria.Vigencia, l => PrecisionSensor.Mediana(Ventana(l).Select(x => x.Fc))).Valor,
            Conciencia: Manual(o => o.Conciencia).Valor,
            Temperatura: temperatura.Valor);
        return (parametros, new FuentesSignos(fr.Fuente, pas.Fuente, temperatura.Fuente));
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
