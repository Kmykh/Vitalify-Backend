using Vitalify.Domain.Pacientes;

namespace Vitalify.Application.Pacientes;

/// <summary>Modelo de lectura de una hospitalización activa con su paciente, cama y sensor vigente.</summary>
public sealed record HospitalizacionActivaLectura(
    Guid PacienteId,
    string NombreCompleto,
    TipoDocumento TipoDocumento,
    string NumeroDocumento,
    DateOnly FechaNacimiento,
    bool FechaNacimientoEstimada,
    Guid HospitalizacionId,
    string DiagnosticoIngreso,
    DateTime IngresoEn,
    Guid CamaId,
    string CodigoCama,
    string Servicio,
    Guid? DispositivoId,
    string? CodigoDispositivo,
    DateTime? DispositivoAsignadoEn);

public sealed record CamaAsignadaDto(Guid Id, string Codigo, string Servicio);

public sealed record DispositivoVinculadoDto(Guid Id, string Codigo, DateTime AsignadoEn);

public sealed record HospitalizacionActivaDto(Guid Id, string DiagnosticoIngreso, DateTime IngresoEn, CamaAsignadaDto Cama, DispositivoVinculadoDto? Dispositivo);

/// <summary>Ficha del paciente con su hospitalización activa (null si no está hospitalizado).</summary>
public sealed record FichaPacienteDto(
    Guid Id,
    string NombreCompleto,
    string TipoDocumento,
    string NumeroDocumento,
    DateOnly FechaNacimiento,
    bool FechaNacimientoEstimada,
    int Edad,
    HospitalizacionActivaDto? HospitalizacionActiva)
{
    public static FichaPacienteDto Desde(Paciente paciente, HospitalizacionActivaLectura? lectura, DateOnly hoy) =>
        new(paciente.Id, paciente.NombreCompleto, paciente.TipoDocumento.ToString(), paciente.NumeroDocumento,
            paciente.FechaNacimiento, paciente.FechaNacimientoEstimada, paciente.Edad(hoy),
            lectura is null ? null : new HospitalizacionActivaDto(
                lectura.HospitalizacionId, lectura.DiagnosticoIngreso, lectura.IngresoEn,
                new CamaAsignadaDto(lectura.CamaId, lectura.CodigoCama, lectura.Servicio),
                lectura.DispositivoId is { } dispositivoId
                    ? new DispositivoVinculadoDto(dispositivoId, lectura.CodigoDispositivo!, lectura.DispositivoAsignadoEn!.Value)
                    : null));
}

public sealed record PacienteHospitalizadoDto(
    Guid PacienteId, string NombreCompleto, int Edad, string Cama, string Servicio,
    string DiagnosticoIngreso, DateTime IngresoEn, string? CodigoDispositivo)
{
    public static PacienteHospitalizadoDto Desde(HospitalizacionActivaLectura l, DateOnly hoy) =>
        new(l.PacienteId, l.NombreCompleto, Paciente.CalcularEdad(l.FechaNacimiento, hoy), l.CodigoCama, l.Servicio,
            l.DiagnosticoIngreso, l.IngresoEn, l.CodigoDispositivo);
}

/// <summary>Fila de la lista de monitoreo (HU07).</summary>
/// <param name="NivelRiesgo">Hasta la fase 4 siempre es <c>sin-datos</c>.</param>
/// <param name="Senal"><c>con-datos</c>, <c>sin-datos</c> (nunca llegó una lectura) o <c>sin-senal</c> (nada en 2 × la vigencia).</param>
public sealed record PacienteMonitoreadoDto(
    Guid PacienteId, string NombreCompleto, int Edad, string Cama, string Servicio, string CodigoDispositivo,
    DateTime IngresoEn, int? UltimoNews2, int? UltimoMews, string NivelRiesgo, DateTime? UltimaLecturaEn, string Senal);

public sealed record PacientesMonitoreadosDto(IReadOnlyList<PacienteMonitoreadoDto> Items, string? Mensaje);

public sealed record VinculacionDto(
    Guid PacienteId, Guid HospitalizacionId, Guid DispositivoId, string CodigoDispositivo, string Cama, DateTime AsignadoEn);

/// <param name="DispositivoLiberado">Código del sensor que se liberó en el egreso, si tenía uno.</param>
public sealed record EgresoDto(Guid PacienteId, Guid HospitalizacionId, DateTime EgresoEn, string Motivo, string? DispositivoLiberado);

/// <summary>Resultado de "¿qué hospitalización activa corresponde a este dispositivo?", para la ingesta (fase 3).</summary>
public sealed record HospitalizacionPorDispositivoDto(
    Guid HospitalizacionId, Guid PacienteId, Guid CamaId, Guid DispositivoId, Guid AsignacionId, DateTime AsignadoEn);
