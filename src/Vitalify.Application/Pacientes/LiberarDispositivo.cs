using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Hospitalizaciones;

namespace Vitalify.Application.Pacientes;

public sealed record LiberarDispositivoComando(Guid PacienteId, Guid UsuarioId, string? Ip);

/// <summary>HU06: desvincula el sensor del paciente; el sensor vuelve a Disponible y la asignación queda en el historial.</summary>
public sealed class LiberarDispositivo(
    IRepositorioPacientes pacientes,
    IRepositorioDispositivos dispositivos,
    IRepositorioHospitalizaciones hospitalizaciones,
    IRepositorioAuditoria auditoria,
    IUnidadDeTrabajo unidadDeTrabajo,
    IReloj reloj)
{
    public async Task<Resultado<Unidad>> EjecutarAsync(LiberarDispositivoComando comando, CancellationToken ct = default)
    {
        if (await pacientes.ObtenerPorIdAsync(comando.PacienteId, ct) is null)
        {
            return ErroresPaciente.NoEncontrado;
        }

        var hospitalizacion = await hospitalizaciones.ObtenerActivaDePacienteAsync(comando.PacienteId, ct);
        if (hospitalizacion is null)
        {
            return ErroresPaciente.SinHospitalizacionActivaNoEncontrada;
        }

        var asignacion = await hospitalizaciones.ObtenerAsignacionVigenteAsync(hospitalizacion.Id, ct);
        if (asignacion is null)
        {
            return ErroresPaciente.SinDispositivo;
        }

        var ahora = reloj.AhoraUtc;
        var dispositivo = (await dispositivos.ObtenerPorIdAsync(asignacion.DispositivoId, ct))!;
        asignacion.Liberar(dispositivo, MotivoLiberacion.Manual, ahora, comando.UsuarioId);
        auditoria.Agregar(RegistroAuditoria.Registrar(
            AccionAuditoria.DispositivoLiberado, ahora, comando.UsuarioId, comando.Ip,
            $"Dispositivo {dispositivo.Id} liberado de la hospitalización {hospitalizacion.Id} (manual)."));

        return await unidadDeTrabajo.GuardarCambiosAsync(ct);
    }
}
