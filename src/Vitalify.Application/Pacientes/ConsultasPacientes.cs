using FluentValidation;
using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Application.Telemetria;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Dispositivos;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Application.Pacientes;

public sealed record ListarPacientesHospitalizadosConsulta(int Pagina = 1, int Tamano = 20, string? Buscar = null);

public sealed class ListarPacientesHospitalizadosValidador : AbstractValidator<ListarPacientesHospitalizadosConsulta>
{
    public const int TamanoMaximo = 100;

    public ListarPacientesHospitalizadosValidador()
    {
        RuleFor(c => c.Pagina).GreaterThanOrEqualTo(1).WithMessage("La página debe ser 1 o mayor.");
        RuleFor(c => c.Tamano).InclusiveBetween(1, TamanoMaximo).WithMessage($"El tamaño de página debe estar entre 1 y {TamanoMaximo}.");
        RuleFor(c => c.Buscar).MaximumLength(100).WithMessage("La búsqueda no puede superar 100 caracteres.");
    }
}

/// <summary>Pacientes con hospitalización activa, ordenados por cama. Busca por nombre, cama o documento.</summary>
public sealed class ListarPacientesHospitalizados(
    IValidator<ListarPacientesHospitalizadosConsulta> validador, IRepositorioHospitalizaciones hospitalizaciones, IReloj reloj)
{
    public async Task<Resultado<Pagina<PacienteHospitalizadoDto>>> EjecutarAsync(
        ListarPacientesHospitalizadosConsulta consulta, CancellationToken ct = default)
    {
        if (await validador.ValidarAsync(consulta, ct) is { } errorValidacion)
        {
            return errorValidacion;
        }

        var hoy = DateOnly.FromDateTime(reloj.AhoraUtc);
        var pagina = await hospitalizaciones.ListarActivasAsync(consulta.Pagina, consulta.Tamano, consulta.Buscar?.Trim(), ct);
        return pagina.Mapear(l => PacienteHospitalizadoDto.Desde(l, hoy));
    }
}

/// <summary>HU07: pacientes activos con un sensor vinculado, con el estado de su señal (sin alertas todavía).</summary>
public sealed class ListarPacientesMonitoreados(
    IRepositorioHospitalizaciones hospitalizaciones, IRepositorioEstadoSignos estados, OpcionesTelemetria opciones, IReloj reloj)
{
    public const string SinDatos = "sin-datos";
    public const string MensajeSinPacientes = "No hay pacientes con un sensor vinculado en este momento.";

    public async Task<Resultado<PacientesMonitoreadosDto>> EjecutarAsync(CancellationToken ct = default)
    {
        var ahora = reloj.AhoraUtc;
        var hoy = DateOnly.FromDateTime(ahora);
        var activas = await hospitalizaciones.ListarActivasConDispositivoAsync(ct);
        var estadosSignos = await estados.ObtenerVariosAsync(activas.Select(l => l.HospitalizacionId).ToList(), ct);

        var items = activas
            .Select(l =>
            {
                var ultimaLectura = estadosSignos.GetValueOrDefault(l.HospitalizacionId)?.UltimaLecturaEn;
                return new PacienteMonitoreadoDto(
                    l.PacienteId, l.NombreCompleto, Domain.Pacientes.Paciente.CalcularEdad(l.FechaNacimiento, hoy), l.CodigoCama,
                    l.Servicio, l.CodigoDispositivo!, l.IngresoEn,
                    // TODO fase 4: completar el último NEWS2/MEWS y el nivel de riesgo con la última evaluación.
                    UltimoNews2: null, UltimoMews: null, NivelRiesgo: SinDatos,
                    UltimaLecturaEn: ultimaLectura,
                    Senal: TextosEstado.De(Vigencia.SenalDe(ultimaLectura, ahora, opciones.Vigencia)));
            })
            .ToList();

        return new PacientesMonitoreadosDto(items, items.Count == 0 ? MensajeSinPacientes : null);
    }
}

public sealed record ObtenerPacienteConsulta(Guid PacienteId, Guid UsuarioId, string? Ip);

/// <summary>Ficha del paciente. Cada consulta queda en la auditoría (Ley 29733).</summary>
public sealed class ObtenerPaciente(
    IRepositorioPacientes pacientes,
    IRepositorioHospitalizaciones hospitalizaciones,
    IRepositorioAuditoria auditoria,
    IUnidadDeTrabajo unidadDeTrabajo,
    IReloj reloj)
{
    public async Task<Resultado<FichaPacienteDto>> EjecutarAsync(ObtenerPacienteConsulta consulta, CancellationToken ct = default)
    {
        var paciente = await pacientes.ObtenerPorIdAsync(consulta.PacienteId, ct);
        if (paciente is null)
        {
            return ErroresPaciente.NoEncontrado;
        }

        var ahora = reloj.AhoraUtc;
        var lectura = await hospitalizaciones.ObtenerLecturaActivaAsync(paciente.Id, ct);
        auditoria.Agregar(RegistroAuditoria.Registrar(
            AccionAuditoria.ConsultaFichaPaciente, ahora, consulta.UsuarioId, consulta.Ip, $"Paciente {paciente.Id}."));

        var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return guardado.EsExito ? FichaPacienteDto.Desde(paciente, lectura, DateOnly.FromDateTime(ahora)) : guardado.Error;
    }
}

/// <summary>
/// Preparación para la fase 3: la ingesta recibirá telemetría con el código del dispositivo (tópico
/// <c>device/{id}/telemetria</c>) y necesita saber a qué hospitalización activa pertenece. Uso interno, sin endpoint.
/// </summary>
public sealed class ObtenerHospitalizacionActivaPorDispositivo(IRepositorioHospitalizaciones hospitalizaciones)
{
    /// <returns>La hospitalización con el sensor vinculado, o null si el código no es válido o no está vinculado.</returns>
    public Task<HospitalizacionPorDispositivoDto?> EjecutarAsync(string codigoDispositivo, CancellationToken ct = default) =>
        Dispositivo.CodigoValido(codigoDispositivo)
            ? hospitalizaciones.ObtenerActivaPorCodigoDispositivoAsync(Dispositivo.NormalizarCodigo(codigoDispositivo), ct)
            : Task.FromResult<HospitalizacionPorDispositivoDto?>(null);
}
