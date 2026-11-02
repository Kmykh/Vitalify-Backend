using FluentValidation;
using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Application.Vinculacion;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Dispositivos;
using Vitalify.Domain.Hospitalizaciones;

namespace Vitalify.Application.Pacientes;

/// <param name="Motivo"><c>AltaMedica</c>, <c>Traslado</c>, <c>Fallecimiento</c>, <c>Voluntaria</c> u <c>Otro</c>.</param>
public sealed record RegistrarEgresoComando(Guid PacienteId, string Motivo, string? Observacion, Guid UsuarioId, string? Ip);

public sealed class RegistrarEgresoValidador : AbstractValidator<RegistrarEgresoComando>
{
    public RegistrarEgresoValidador()
    {
        RuleFor(c => c.Motivo)
            .Must(m => Enumeraciones.TryParseNombre<MotivoEgreso>(m, out _))
            .WithMessage($"El motivo debe ser uno de: {Enumeraciones.Nombres<MotivoEgreso>()}.");

        RuleFor(c => c.Observacion)
            .Must(o => o is null || o.Trim().Length <= Hospitalizacion.LargoMaximoObservacion)
            .WithMessage($"La observación no puede superar {Hospitalizacion.LargoMaximoObservacion} caracteres.");
    }
}

/// <summary>
/// HU08: registra el egreso. Si el paciente tiene un sensor vinculado, lo libera primero; todo se guarda en una
/// sola unidad de trabajo (una transacción). La hospitalización queda Finalizada y se conserva. El wearable liberado
/// recibe <c>desvinculado</c> y un QR nuevo.
/// </summary>
public sealed class RegistrarEgreso(
    IValidator<RegistrarEgresoComando> validador,
    IRepositorioPacientes pacientes,
    IRepositorioDispositivos dispositivos,
    IRepositorioHospitalizaciones hospitalizaciones,
    IRepositorioAuditoria auditoria,
    IUnidadDeTrabajo unidadDeTrabajo,
    IResolutorDispositivos resolutorDispositivos,
    ServicioCodigosVinculacion servicioCodigos,
    IReloj reloj)
{
    public async Task<Resultado<EgresoDto>> EjecutarAsync(RegistrarEgresoComando comando, CancellationToken ct = default)
    {
        if (await validador.ValidarAsync(comando, ct) is { } errorValidacion)
        {
            return errorValidacion;
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

        var ahora = reloj.AhoraUtc;
        Dispositivo? liberado = null;

        var asignacion = await hospitalizaciones.ObtenerAsignacionVigenteAsync(hospitalizacion.Id, ct);
        if (asignacion is not null)
        {
            var dispositivo = (await dispositivos.ObtenerPorIdAsync(asignacion.DispositivoId, ct))!;
            asignacion.Liberar(dispositivo, MotivoLiberacion.Egreso, ahora, comando.UsuarioId);
            liberado = dispositivo;
            auditoria.Agregar(RegistroAuditoria.Registrar(
                AccionAuditoria.DispositivoLiberado, ahora, comando.UsuarioId, comando.Ip,
                $"Dispositivo {dispositivo.Id} liberado de la hospitalización {hospitalizacion.Id} (egreso)."));
        }

        Enumeraciones.TryParseNombre<MotivoEgreso>(comando.Motivo, out var motivo);
        hospitalizacion.RegistrarEgreso(motivo, comando.Observacion, ahora, comando.UsuarioId);
        auditoria.Agregar(RegistroAuditoria.Registrar(
            AccionAuditoria.PacienteEgresado, ahora, comando.UsuarioId, comando.Ip,
            $"Paciente {hospitalizacion.PacienteId}, hospitalización {hospitalizacion.Id}, motivo {motivo}."));

        var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
        if (!guardado.EsExito)
        {
            return guardado.Error;
        }

        if (liberado is not null)
        {
            resolutorDispositivos.Invalidar(liberado.Codigo);
            await servicioCodigos.AlLiberarAsync(liberado, comando.UsuarioId, comando.Ip, ct);
        }

        return new EgresoDto(hospitalizacion.PacienteId, hospitalizacion.Id, ahora, motivo.ToString(), liberado?.Codigo);
    }
}
