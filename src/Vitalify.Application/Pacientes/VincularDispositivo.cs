using Vitalify.Application.Comun;
using Vitalify.Application.Dispositivos;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Dispositivos;
using Vitalify.Domain.Hospitalizaciones;

namespace Vitalify.Application.Pacientes;

public sealed record VincularDispositivoComando(Guid PacienteId, Guid? DispositivoId, Guid UsuarioId, string? Ip);

/// <summary>
/// HU06: vincula un sensor disponible a la hospitalización activa del paciente. Si el sensor ya está en otra cama,
/// responde 409 indicando la cama (nunca el paciente). Vincular de nuevo el mismo sensor es idempotente.
/// </summary>
public sealed class VincularDispositivo(
    IRepositorioPacientes pacientes,
    IRepositorioDispositivos dispositivos,
    IRepositorioCamas camas,
    IRepositorioHospitalizaciones hospitalizaciones,
    IRepositorioAuditoria auditoria,
    IUnidadDeTrabajo unidadDeTrabajo,
    IResolutorDispositivos resolutorDispositivos,
    IReloj reloj)
{
    public async Task<Resultado<VinculacionDto>> EjecutarAsync(VincularDispositivoComando comando, CancellationToken ct = default)
    {
        if (comando.DispositivoId is not { } dispositivoId || dispositivoId == Guid.Empty)
        {
            return ErroresComunes.ValidacionDeCampo("dispositivoId", "El dispositivo es obligatorio.");
        }

        if (await pacientes.ObtenerPorIdAsync(comando.PacienteId, ct) is null)
        {
            return ErroresPaciente.NoEncontrado;
        }

        var hospitalizacion = await hospitalizaciones.ObtenerActivaDePacienteAsync(comando.PacienteId, ct);
        if (hospitalizacion is null)
        {
            return ErroresPaciente.SinHospitalizacionActiva;
        }

        var dispositivo = await dispositivos.ObtenerPorIdAsync(dispositivoId, ct);
        if (dispositivo is null)
        {
            return ErroresDispositivo.NoEncontrado;
        }

        var cama = (await camas.ObtenerPorIdAsync(hospitalizacion.CamaId, ct))!;
        var vigente = await hospitalizaciones.ObtenerAsignacionVigenteAsync(hospitalizacion.Id, ct);
        if (vigente is not null)
        {
            return vigente.DispositivoId == dispositivo.Id
                ? Dto(vigente, dispositivo, cama.Codigo, hospitalizacion)
                : ErroresPaciente.YaTieneDispositivo;
        }

        switch (dispositivo.Estado)
        {
            case EstadoDispositivo.Asignado:
                var otraCama = await hospitalizaciones.ObtenerCamaConDispositivoAsync(dispositivo.Id, ct);
                return otraCama is null ? ErroresDispositivo.YaVinculado : ErroresDispositivo.YaVinculadoA(dispositivo.Codigo, otraCama);
            case EstadoDispositivo.Mantenimiento:
                return ErroresDispositivo.EnMantenimiento;
            case EstadoDispositivo.DadoDeBaja:
                return ErroresDispositivo.DadoDeBaja;
        }

        var ahora = reloj.AhoraUtc;
        var asignacion = AsignacionDispositivo.Vincular(dispositivo, hospitalizacion, ahora, comando.UsuarioId);
        hospitalizaciones.AgregarAsignacion(asignacion);
        auditoria.Agregar(RegistroAuditoria.Registrar(
            AccionAuditoria.DispositivoVinculado, ahora, comando.UsuarioId, comando.Ip,
            $"Dispositivo {dispositivo.Id} vinculado a la hospitalización {hospitalizacion.Id} (paciente {hospitalizacion.PacienteId})."));

        var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
        if (!guardado.EsExito)
        {
            return guardado.Error;
        }

        resolutorDispositivos.Invalidar(dispositivo.Codigo);
        return Dto(asignacion, dispositivo, cama.Codigo, hospitalizacion);
    }

    private static VinculacionDto Dto(AsignacionDispositivo asignacion, Dispositivo dispositivo, string cama, Hospitalizacion hospitalizacion) =>
        new(hospitalizacion.PacienteId, hospitalizacion.Id, dispositivo.Id, dispositivo.Codigo, cama, asignacion.AsignadoEn);
}
