using Vitalify.Application.Comun;
using Vitalify.Application.Dispositivos;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Dispositivos;
using Vitalify.Domain.Pacientes;
using Vitalify.Domain.Vinculacion;

namespace Vitalify.Application.Vinculacion;

/// <summary>
/// Lo que se le dice a la pantalla del wearable: un QR nuevo, "vinculado" con cama e iniciales, o "desvinculado". Lo
/// comparten los casos de uso de vinculación, egreso y liberación, para que el estado de la pantalla siga siempre al
/// de la base.
/// </summary>
public sealed class ServicioCodigosVinculacion(
    IRepositorioCodigosVinculacion codigos,
    IRepositorioHospitalizaciones hospitalizaciones,
    IRepositorioAuditoria auditoria,
    IUnidadDeTrabajo unidadDeTrabajo,
    IPublicadorComandosDispositivo publicador,
    OpcionesVinculacion opciones,
    IReloj reloj)
{
    /// <summary>
    /// Genera un código nuevo (invalida el anterior), lo guarda como hash, lo audita como <c>QrGenerado</c> y publica
    /// <c>mostrar-qr</c> retenido. Solo para dispositivos disponibles.
    /// </summary>
    public async Task<Resultado<QrGeneradoDto>> GenerarYPublicarAsync(
        Dispositivo dispositivo, string motivo, Guid? usuarioId, string? ip, CancellationToken ct = default)
    {
        if (!dispositivo.PuedeAsignarse)
        {
            return dispositivo.Estado switch
            {
                EstadoDispositivo.Asignado => ErroresDispositivo.YaVinculado,
                EstadoDispositivo.Mantenimiento => ErroresDispositivo.EnMantenimiento,
                _ => ErroresDispositivo.DadoDeBaja,
            };
        }

        var ahora = reloj.AhoraUtc;
        (await codigos.ObtenerActivoAsync(dispositivo.Id, ct))?.Invalidar(ahora);
        var (codigo, enClaro) = CodigoVinculacion.Generar(dispositivo.Id, ahora, opciones.VigenciaCodigo);
        codigos.Agregar(codigo);
        auditoria.Agregar(RegistroAuditoria.Registrar(
            AccionAuditoria.QrGenerado, ahora, usuarioId, ip, $"Código {codigo.Id} del dispositivo {dispositivo.Id} ({motivo})."));

        var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
        if (!guardado.EsExito)
        {
            return guardado.Error;
        }

        await publicador.PublicarAsync(
            dispositivo.Codigo, new MostrarQr(FormatoQr.Componer(dispositivo.Codigo, enClaro), codigo.ExpiraEn), retenido: true, ct);
        return new QrGeneradoDto(dispositivo.Id, dispositivo.Codigo, codigo.ExpiraEn);
    }

    /// <summary>Vuelve a publicar <c>vinculado</c> (por ejemplo, si el ESP32 se reinició).</summary>
    public async Task PublicarVinculadoAsync(Dispositivo dispositivo, CancellationToken ct = default)
    {
        if (await hospitalizaciones.ObtenerVinculoDispositivoAsync(dispositivo.Id, ct) is { } vinculo)
        {
            await PublicarVinculadoAsync(dispositivo.Codigo, vinculo.CodigoCama, vinculo.NombrePaciente, ct);
        }
    }

    /// <summary>Publica <c>vinculado</c> retenido con la cama y las iniciales: es lo único del paciente que va al wearable.</summary>
    public Task PublicarVinculadoAsync(string codigoDispositivo, string cama, string nombrePaciente, CancellationToken ct = default) =>
        publicador.PublicarAsync(codigoDispositivo, new Vinculado(cama, Paciente.CalcularIniciales(nombrePaciente)), retenido: true, ct);

    /// <summary>
    /// Tras liberar el sensor (egreso o desvinculación) publica <c>desvinculado</c> y un QR nuevo, para que quede listo
    /// para el siguiente paciente. Si el QR no se puede generar ahora, lo genera el worker de renovación cuando el
    /// wearable esté en línea, o el propio wearable lo pide con <c>solicitar-qr</c>.
    /// </summary>
    public async Task AlLiberarAsync(Dispositivo dispositivo, Guid usuarioId, string? ip, CancellationToken ct = default)
    {
        await publicador.PublicarAsync(dispositivo.Codigo, new Desvinculado(), retenido: true, ct);
        await GenerarYPublicarAsync(dispositivo, "sensor liberado", usuarioId, ip, ct);
    }
}
