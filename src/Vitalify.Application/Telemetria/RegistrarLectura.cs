using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Application.Telemetria;

/// <summary>
/// Único puerto de entrada de la telemetría (HU10, HU11 y HU12). Lo llaman el simulador, el endpoint de desarrollo
/// y, en la fase 7, el adaptador MQTT. El núcleo no sabe quién envió la lectura: solo guarda su origen.
/// </summary>
public interface IRegistrarLectura
{
    Task<Resultado<ResultadoIngesta>> EjecutarAsync(LecturaTelemetria lectura, CancellationToken ct = default);
}

public static class ErroresTelemetria
{
    public static readonly Error LecturaDuplicada =
        Error.Conflicto("lectura-duplicada", "Ya existe una lectura de ese dispositivo con esa marca de tiempo.");
}

public sealed class RegistrarLectura(
    IResolutorDispositivos resolutor,
    IRepositorioLecturas lecturas,
    IRepositorioEstadoSignos estados,
    IRepositorioIncidencias incidencias,
    IUnidadDeTrabajoHistorial unidadDeTrabajo,
    ISolicitudNuevaLectura solicitudNuevaLectura,
    IEnumerable<IManejadorLecturaRegistrada> manejadores,
    RangosFisiologicos rangos,
    OpcionesTelemetria opciones,
    IReloj reloj) : IRegistrarLectura
{
    private const int IntentosMaximos = 3;

    public async Task<Resultado<ResultadoIngesta>> EjecutarAsync(LecturaTelemetria lectura, CancellationToken ct = default)
    {
        var ahora = reloj.AhoraUtc;
        var codigo = lectura.CodigoDispositivo;

        if (opciones.Ventana.Validar(lectura.MedidoEn, ahora) is { } motivo)
        {
            return await RechazarAsync(lectura, [IncidenciaTelemetria.Registrar(ahora, codigo, null, TipoIncidencia.MarcaDeTiempoInvalida, motivo)], ct);
        }

        var resolucion = await resolutor.ResolverAsync(codigo, ct);
        if (resolucion.Hospitalizacion is not { } hospitalizacion)
        {
            var (tipo, detalle) = resolucion.Estado == EstadoDispositivoEnIngesta.Desconocido
                ? (TipoIncidencia.DispositivoDesconocido, $"El dispositivo {codigo} no está registrado.")
                : (TipoIncidencia.DispositivoSinVincular, $"El dispositivo {codigo} no está vinculado a una hospitalización activa.");
            return await RechazarAsync(lectura, [IncidenciaTelemetria.Registrar(ahora, codigo, null, tipo, detalle)], ct);
        }

        if (await lecturas.ExisteAsync(codigo, lectura.MedidoEn, ct))
        {
            return ResultadoIngesta.De(EstadoIngesta.Duplicada, lectura, []);
        }

        var evaluacion = EvaluadorSignos.Evaluar(lectura.Signos, rangos);
        var incidenciasLectura = evaluacion.Descartes
            .Select(d => IncidenciaTelemetria.Desde(d, ahora, codigo, hospitalizacion.HospitalizacionId))
            .ToList();

        if (!evaluacion.Validos.TieneAlgunSigno && !lectura.Caida)
        {
            var rechazo = await RechazarAsync(lectura, incidenciasLectura, ct);
            await SolicitarNuevaLecturaSiHaceFaltaAsync(evaluacion, lectura, ct);
            return rechazo;
        }

        var registro = LecturaSignos.Registrar(
            codigo, lectura.MedidoEn, ahora, hospitalizacion.HospitalizacionId, hospitalizacion.PacienteId,
            evaluacion.Validos, lectura.Caida, lectura.Seq, lectura.Origen);

        for (var intento = 1; ; intento++)
        {
            lecturas.Agregar(registro);
            var estado = await estados.ObtenerAsync(hospitalizacion.HospitalizacionId, ct);
            if (estado is null)
            {
                estado = EstadoSignosActual.Iniciar(hospitalizacion.HospitalizacionId, hospitalizacion.PacienteId, codigo);
                estados.Agregar(estado);
            }

            estado.Aplicar(registro);
            incidenciasLectura.ForEach(incidencias.Agregar);

            var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
            if (guardado.EsExito)
            {
                break;
            }

            if (guardado.Error.Codigo == ErroresTelemetria.LecturaDuplicada.Codigo)
            {
                return ResultadoIngesta.De(EstadoIngesta.Duplicada, lectura, []);
            }

            // Otra lectura actualizó el estado a la vez: se reintenta con el estado recargado.
            if (guardado.Error.Codigo != ErroresComunes.Concurrencia.Codigo || intento == IntentosMaximos)
            {
                return guardado.Error;
            }
        }

        await SolicitarNuevaLecturaSiHaceFaltaAsync(evaluacion, lectura, ct);

        var evento = new LecturaRegistrada(
            hospitalizacion.HospitalizacionId, hospitalizacion.PacienteId, codigo, lectura.MedidoEn, evaluacion.Validos, lectura.Caida);
        foreach (var manejador in manejadores)
        {
            await manejador.ManejarAsync(evento, ct);
        }

        var estadoIngesta = incidenciasLectura.Count == 0 ? EstadoIngesta.Aceptada : EstadoIngesta.AceptadaParcial;
        return ResultadoIngesta.De(estadoIngesta, lectura, incidenciasLectura);
    }

    private async Task<Resultado<ResultadoIngesta>> RechazarAsync(
        LecturaTelemetria lectura, IReadOnlyList<IncidenciaTelemetria> incidenciasRechazo, CancellationToken ct)
    {
        foreach (var incidencia in incidenciasRechazo)
        {
            incidencias.Agregar(incidencia);
        }

        var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return guardado.EsExito ? ResultadoIngesta.De(EstadoIngesta.Rechazada, lectura, incidenciasRechazo) : guardado.Error;
    }

    private Task SolicitarNuevaLecturaSiHaceFaltaAsync(EvaluacionSignos evaluacion, LecturaTelemetria lectura, CancellationToken ct) =>
        evaluacion.Descartes.FirstOrDefault(d => d.RequiereNuevaLectura) is { } descarte
            ? solicitudNuevaLectura.SolicitarAsync(new SolicitudNuevaLectura(lectura.CodigoDispositivo, lectura.MedidoEn, descarte.Detalle), ct)
            : Task.CompletedTask;
}
