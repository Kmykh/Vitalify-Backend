using FluentValidation;
using Vitalify.Application.Camas;
using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Hospitalizaciones;
using Vitalify.Domain.Pacientes;

namespace Vitalify.Application.Pacientes;

/// <param name="TipoDocumento"><c>Dni</c>, <c>CarneExtranjeria</c> o <c>Pasaporte</c>.</param>
/// <param name="UsuarioId">Enfermera que registra el ingreso.</param>
public sealed record RegistrarIngresoPacienteComando(
    string NombreCompleto,
    string TipoDocumento,
    string NumeroDocumento,
    string? FechaNacimiento,
    int? Edad,
    Guid? CamaId,
    string DiagnosticoIngreso,
    Guid UsuarioId,
    string? Ip) : IDatosBasicosPaciente;

public sealed class RegistrarIngresoPacienteValidador : DatosBasicosPacienteValidador<RegistrarIngresoPacienteComando>
{
    public RegistrarIngresoPacienteValidador(IReloj reloj)
        : base(reloj)
    {
        RuleFor(c => c.TipoDocumento)
            .Must(t => Enumeraciones.TryParseNombre<TipoDocumento>(t, out _))
            .WithMessage("El tipo de documento debe ser Dni, CarneExtranjeria o Pasaporte.");

        RuleFor(c => c.NumeroDocumento)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("El número de documento es obligatorio.")
            .Must((c, n) => !Enumeraciones.TryParseNombre<TipoDocumento>(c.TipoDocumento, out var tipo) || Paciente.DocumentoValido(tipo, n))
            .WithMessage(c => Enumeraciones.TryParseNombre<TipoDocumento>(c.TipoDocumento, out var tipo) && tipo == Domain.Pacientes.TipoDocumento.Dni
                ? "El DNI debe tener 8 dígitos."
                : "El documento debe tener de 6 a 12 letras o números.");

        RuleFor(c => c.CamaId)
            .Must(id => id is not null && id != Guid.Empty).WithMessage("La cama es obligatoria.");
    }
}

/// <summary>
/// HU05: registra el ingreso. Crea al paciente o reutiliza el existente por su documento (sin modificar sus datos)
/// y abre la hospitalización en una cama activa y libre.
/// </summary>
public sealed class RegistrarIngresoPaciente(
    IValidator<RegistrarIngresoPacienteComando> validador,
    IRepositorioPacientes pacientes,
    IRepositorioCamas camas,
    IRepositorioHospitalizaciones hospitalizaciones,
    IRepositorioAuditoria auditoria,
    IUnidadDeTrabajo unidadDeTrabajo,
    IReloj reloj)
{
    public async Task<Resultado<FichaPacienteDto>> EjecutarAsync(RegistrarIngresoPacienteComando comando, CancellationToken ct = default)
    {
        if (await validador.ValidarAsync(comando, ct) is { } errorValidacion)
        {
            return errorValidacion;
        }

        var cama = await camas.ObtenerPorIdAsync(comando.CamaId!.Value, ct);
        if (cama is null || !cama.Activa)
        {
            return ErroresComunes.ValidacionDeCampo("camaId", cama is null ? "La cama no existe." : "La cama no está activa.");
        }

        if (await hospitalizaciones.CamaOcupadaAsync(cama.Id, ct))
        {
            return ErroresCama.Ocupada;
        }

        Enumeraciones.TryParseNombre<TipoDocumento>(comando.TipoDocumento, out var tipoDocumento);
        var ahora = reloj.AhoraUtc;
        var paciente = await pacientes.ObtenerPorDocumentoAsync(tipoDocumento, Paciente.NormalizarDocumento(comando.NumeroDocumento), ct);

        if (paciente is null)
        {
            var (fecha, estimada) = DatosBasicosPacienteValidador<RegistrarIngresoPacienteComando>.ResolverFechaNacimiento(comando, DateOnly.FromDateTime(ahora));
            paciente = Paciente.Registrar(comando.NombreCompleto, tipoDocumento, comando.NumeroDocumento, fecha, estimada, ahora);
            pacientes.Agregar(paciente);
        }
        else if (await hospitalizaciones.ObtenerActivaDePacienteAsync(paciente.Id, ct) is not null)
        {
            return ErroresPaciente.YaHospitalizado;
        }

        var hospitalizacion = Hospitalizacion.Abrir(paciente.Id, cama.Id, comando.DiagnosticoIngreso, ahora, comando.UsuarioId);
        hospitalizaciones.Agregar(hospitalizacion);
        auditoria.Agregar(RegistroAuditoria.Registrar(
            AccionAuditoria.PacienteIngresado, ahora, comando.UsuarioId, comando.Ip,
            $"Paciente {paciente.Id}, hospitalización {hospitalizacion.Id}, cama {cama.Id}."));

        var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
        if (!guardado.EsExito)
        {
            return guardado.Error;
        }

        var lectura = new HospitalizacionActivaLectura(
            paciente.Id, paciente.NombreCompleto, paciente.TipoDocumento, paciente.NumeroDocumento, paciente.FechaNacimiento,
            paciente.FechaNacimientoEstimada, hospitalizacion.Id, hospitalizacion.DiagnosticoIngreso, hospitalizacion.IngresoEn,
            cama.Id, cama.Codigo, cama.Servicio, null, null, null);
        return FichaPacienteDto.Desde(paciente, lectura, DateOnly.FromDateTime(ahora));
    }
}
