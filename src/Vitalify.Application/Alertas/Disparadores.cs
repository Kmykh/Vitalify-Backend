using System.Globalization;
using Vitalify.Application.Clinica;
using Vitalify.Application.Puertos;
using Vitalify.Application.Telemetria;
using Vitalify.Domain.Alertas;
using Vitalify.Domain.Clinica;

namespace Vitalify.Application.Alertas;

/// <summary>
/// Evento de aplicación que se publica con cada evaluación de riesgo. Las del wearable que no cambian nada no se guardan
/// (<see cref="EvaluacionRiesgo.AportaRespectoDe"/>): entonces <see cref="EvaluacionId"/> es la última guardada, que tiene
/// el mismo puntaje.
/// </summary>
/// <param name="Guardada">Si esta evaluación se guardó (cambió algo): las sugerencias NNN solo se recalculan entonces.</param>
public sealed record RiesgoEvaluado(
    Guid HospitalizacionId, Guid PacienteId, Guid EvaluacionId, DateTime EvaluadaEn, int News2, NivelRiesgoNews2 News2Nivel,
    int Mews, NivelRiesgoMews MewsNivel, bool Completo, OrigenEvaluacion Origen, bool Guardada = true)
{
    public static RiesgoEvaluado Desde(EvaluacionRiesgo e) =>
        new(e.HospitalizacionId, e.PacienteId, e.Id, e.EvaluadaEn, e.News2Total, e.News2Nivel, e.MewsTotal, e.MewsNivel,
            e.News2Completo && e.MewsCompleto, e.Origen);
}

public interface IManejadorRiesgoEvaluado
{
    Task ManejarAsync(RiesgoEvaluado evento, CancellationToken ct = default);
}

/// <summary>Publica <see cref="RiesgoEvaluado"/>. Un manejador que falla queda en la bitácora y no interrumpe a los demás.</summary>
public sealed class PublicadorRiesgoEvaluado(IEnumerable<IManejadorRiesgoEvaluado> manejadores, IBitacoraFallos bitacora)
{
    /// <param name="evaluacionGuardadaId">La evaluación guardada que respalda el puntaje, si no es la misma (ver <see cref="RiesgoEvaluado"/>).</param>
    public async Task PublicarAsync(EvaluacionRiesgo evaluacion, Guid? evaluacionGuardadaId = null, CancellationToken ct = default)
    {
        var evento = RiesgoEvaluado.Desde(evaluacion) with
        {
            EvaluacionId = evaluacionGuardadaId ?? evaluacion.Id,
            Guardada = evaluacionGuardadaId is null || evaluacionGuardadaId == evaluacion.Id,
        };
        foreach (var manejador in manejadores)
        {
            try
            {
                await manejador.ManejarAsync(evento, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                bitacora.Registrar($"{manejador.GetType().Name} de la evaluación {evaluacion.Id}", ex);
            }
        }
    }
}

/// <summary>NEWS2 bajo-medio, medio o alto (o MEWS medio o alto) → alerta de riesgo con la prioridad mayor.</summary>
public sealed class AlertaDeRiesgoAlEvaluar(ServicioAlertas alertas) : IManejadorRiesgoEvaluado
{
    public async Task ManejarAsync(RiesgoEvaluado evento, CancellationToken ct = default)
    {
        if (PrioridadRiesgo.Calcular(evento.News2Nivel, evento.MewsNivel) is not { } riesgo)
        {
            return;
        }

        await alertas.DispararAsync(new DisparoAlerta(
            evento.HospitalizacionId, TipoAlerta.Riesgo, riesgo.Prioridad, riesgo.Nivel, evento.News2, evento.Mews,
            new Dictionary<string, string>
            {
                ["news2Nivel"] = TextosClinicos.De(evento.News2Nivel),
                ["mewsNivel"] = TextosClinicos.De(evento.MewsNivel),
                ["completo"] = evento.Completo ? "true" : "false",
                ["origen"] = evento.Origen.ToString(),
                ["evaluacionId"] = evento.EvaluacionId.ToString(),
            }), ct);
    }
}

/// <summary>Evento <c>riesgoActualizado</c> al grupo del servicio.</summary>
public sealed class PublicarRiesgoEnTiempoReal(ContextoHospitalizaciones contextos, INotificadorTiempoReal tiempoReal) : IManejadorRiesgoEvaluado
{
    public async Task ManejarAsync(RiesgoEvaluado evento, CancellationToken ct = default)
    {
        if (await contextos.ObtenerAsync(evento.HospitalizacionId, ct) is { } contexto)
        {
            await tiempoReal.RiesgoActualizadoAsync(contexto.ServicioId, new RiesgoActualizadoDto(
                evento.PacienteId, evento.HospitalizacionId, contexto.CodigoCama, evento.News2, evento.Mews,
                TextosClinicos.De(evento.News2Nivel), evento.Completo, evento.EvaluadaEn), ct);
        }
    }
}

/// <summary>Con cada lectura: una caída crea una alerta alta y, si había una alerta de sin señal abierta, se resuelve.</summary>
public sealed class AlertasAlRegistrarLectura(ServicioAlertas alertas) : IManejadorLecturaRegistrada
{
    public async Task ManejarAsync(LecturaRegistrada lectura, CancellationToken ct = default)
    {
        await alertas.ResolverAsync(lectura.HospitalizacionId, TipoAlerta.SinSenal, ct);
        if (lectura.Caida)
        {
            await alertas.DispararAsync(new DisparoAlerta(
                lectura.HospitalizacionId, TipoAlerta.Caida, PrioridadAlerta.Alta,
                Detalle: new Dictionary<string, string>
                {
                    ["dispositivo"] = lectura.CodigoDispositivo,
                    ["medidoEn"] = lectura.MedidoEn.ToString("O", CultureInfo.InvariantCulture),
                }), ct);
        }
    }
}

/// <summary>Como máximo un <c>signosActualizados</c> cada 5 s por paciente (singleton).</summary>
public sealed class LimitadorSignosTiempoReal
{
    public static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(5);

    private readonly Dictionary<Guid, DateTime> _ultimos = [];
    private readonly object _cerrojo = new();

    public bool Permitir(Guid pacienteId, DateTime ahora)
    {
        lock (_cerrojo)
        {
            if (_ultimos.TryGetValue(pacienteId, out var ultimo) && ahora - ultimo < Intervalo)
            {
                return false;
            }

            _ultimos[pacienteId] = ahora;
            return true;
        }
    }
}

/// <summary>Evento <c>signosActualizados</c> al grupo del servicio, limitado a uno cada 5 s por paciente.</summary>
public sealed class PublicarSignosEnTiempoReal(
    ContextoHospitalizaciones contextos, INotificadorTiempoReal tiempoReal, LimitadorSignosTiempoReal limitador, IReloj reloj) : IManejadorLecturaRegistrada
{
    public async Task ManejarAsync(LecturaRegistrada lectura, CancellationToken ct = default)
    {
        if (!limitador.Permitir(lectura.PacienteId, reloj.AhoraUtc) || await contextos.ObtenerAsync(lectura.HospitalizacionId, ct) is not { } contexto)
        {
            return;
        }

        var s = lectura.Signos;
        await tiempoReal.SignosActualizadosAsync(contexto.ServicioId, new SignosActualizadosDto(
            lectura.PacienteId, lectura.HospitalizacionId, contexto.CodigoCama, lectura.MedidoEn,
            s.Fc, s.Fr, s.Spo2, s.Temperatura, s.Pas, s.Pad, lectura.Caida), ct);
    }
}
