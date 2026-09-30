using FluentValidation;
using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Auditoria;

namespace Vitalify.Application.Pacientes;

public sealed record ActualizarDatosPacienteComando(
    Guid PacienteId,
    string NombreCompleto,
    string? FechaNacimiento,
    int? Edad,
    string DiagnosticoIngreso,
    Guid UsuarioId,
    string? Ip) : IDatosBasicosPaciente;

public sealed class ActualizarDatosPacienteValidador(IReloj reloj)
    : DatosBasicosPacienteValidador<ActualizarDatosPacienteComando>(reloj);

/// <summary>HU05: corrige nombre y fecha de nacimiento del paciente y el diagnóstico de la hospitalización activa.</summary>
public sealed class ActualizarDatosPaciente(
    IValidator<ActualizarDatosPacienteComando> validador,
    IRepositorioPacientes pacientes,
    IRepositorioHospitalizaciones hospitalizaciones,
    IRepositorioAuditoria auditoria,
    IUnidadDeTrabajo unidadDeTrabajo,
    IReloj reloj)
{
    public async Task<Resultado<FichaPacienteDto>> EjecutarAsync(ActualizarDatosPacienteComando comando, CancellationToken ct = default)
    {
        if (await validador.ValidarAsync(comando, ct) is { } errorValidacion)
        {
            return errorValidacion;
        }

        var paciente = await pacientes.ObtenerPorIdAsync(comando.PacienteId, ct);
        if (paciente is null)
        {
            return ErroresPaciente.NoEncontrado;
        }

        var hospitalizacion = await hospitalizaciones.ObtenerActivaDePacienteAsync(paciente.Id, ct);
        if (hospitalizacion is null)
        {
            return ErroresPaciente.SinHospitalizacionActivaNoEncontrada;
        }

        var ahora = reloj.AhoraUtc;
        var hoy = DateOnly.FromDateTime(ahora);
        var (fecha, estimada) = DatosBasicosPacienteValidador<ActualizarDatosPacienteComando>.ResolverFechaNacimiento(comando, hoy);
        paciente.ActualizarDatos(comando.NombreCompleto, fecha, estimada, ahora);
        hospitalizacion.ActualizarDiagnostico(comando.DiagnosticoIngreso);
        auditoria.Agregar(RegistroAuditoria.Registrar(
            AccionAuditoria.PacienteActualizado, ahora, comando.UsuarioId, comando.Ip,
            $"Paciente {paciente.Id}, hospitalización {hospitalizacion.Id}."));

        var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
        if (!guardado.EsExito)
        {
            return guardado.Error;
        }

        return FichaPacienteDto.Desde(paciente, await hospitalizaciones.ObtenerLecturaActivaAsync(paciente.Id, ct), hoy);
    }
}
