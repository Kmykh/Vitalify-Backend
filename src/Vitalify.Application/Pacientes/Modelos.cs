using Vitalify.Domain.Clinica;
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
    Guid ServicioId,
    Guid? DispositivoId,
    string? CodigoDispositivo,
    DateTime? DispositivoAsignadoEn);

public sealed record CamaAsignadaDto(Guid Id, string Codigo, string Servicio, Guid ServicioId);

/// <summary>Lo que necesitan las alertas y los eventos de una hospitalización: sin nombre ni documento del paciente.</summary>
public sealed record ContextoHospitalizacion(Guid HospitalizacionId, Guid PacienteId, Guid ServicioId, string CodigoCama, bool Activa);

public sealed record DispositivoVinculadoDto(Guid Id, string Codigo, DateTime AsignadoEn);

public sealed record HospitalizacionActivaDto(Guid Id, string DiagnosticoIngreso, DateTime IngresoEn, CamaAsignadaDto Cama, DispositivoVinculadoDto? Dispositivo);

/// <summary>Ficha del paciente con su hospitalización activa (null si no está hospitalizado).</summary>
/// <param name="ConfusionBasal">Vive con confusión previa: el asistente de conciencia pregunta "¿más confundido que de costumbre?".</param>
public sealed record FichaPacienteDto(
    Guid Id,
    string NombreCompleto,
    string TipoDocumento,
    string NumeroDocumento,
    DateOnly FechaNacimiento,
    bool FechaNacimientoEstimada,
    int Edad,
    bool ConfusionBasal,
    HospitalizacionActivaDto? HospitalizacionActiva)
{
    public static FichaPacienteDto Desde(Paciente paciente, HospitalizacionActivaLectura? lectura, DateOnly hoy) =>
        new(paciente.Id, paciente.NombreCompleto, paciente.TipoDocumento.ToString(), paciente.NumeroDocumento,
            paciente.FechaNacimiento, paciente.FechaNacimientoEstimada, paciente.Edad(hoy), paciente.ConfusionBasal,
            lectura is null ? null : new HospitalizacionActivaDto(
                lectura.HospitalizacionId, lectura.DiagnosticoIngreso, lectura.IngresoEn,
                new CamaAsignadaDto(lectura.CamaId, lectura.CodigoCama, lectura.Servicio, lectura.ServicioId),
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
/// <param name="NivelRiesgo">Nivel NEWS2 de la última evaluación (<c>bajo</c>, <c>bajo-medio</c>, <c>medio</c>, <c>alto</c>) o <c>sin-datos</c>.</param>
/// <param name="EvaluacionCompleta">false si faltaron parámetros (el puntaje es parcial); null sin evaluación.</param>
/// <param name="Senal"><c>con-datos</c>, <c>sin-datos</c> (nunca llegó una lectura) o <c>sin-senal</c> (nada en 2 × la vigencia).</param>
/// <param name="ConexionSensor"><c>en-linea</c>, <c>fuera-de-linea</c> o <c>desconocida</c> (tópico MQTT de estado).</param>
/// <param name="AlertasAbiertas">Cantidad de alertas pendientes o en atención y la prioridad más alta (null si no hay).</param>
public sealed record PacienteMonitoreadoDto(
    Guid PacienteId, string NombreCompleto, int Edad, string Cama, string Servicio, string CodigoDispositivo,
    DateTime IngresoEn, int? UltimoNews2, int? UltimoMews, string NivelRiesgo, bool? EvaluacionCompleta,
    DateTime? UltimaLecturaEn, string Senal, string ConexionSensor, AlertasAbiertasDto AlertasAbiertas);

public sealed record AlertasAbiertasDto(int Cantidad, string? PrioridadMaxima);

public sealed record PacientesMonitoreadosDto(IReadOnlyList<PacienteMonitoreadoDto> Items, string? Mensaje);

public sealed record VinculacionDto(
    Guid PacienteId, Guid HospitalizacionId, Guid DispositivoId, string CodigoDispositivo, string Cama, DateTime AsignadoEn);

/// <param name="DispositivoLiberado">Código del sensor que se liberó en el egreso, si tenía uno.</param>
public sealed record EgresoDto(Guid PacienteId, Guid HospitalizacionId, DateTime EgresoEn, string Motivo, string? DispositivoLiberado);

/// <summary>Resultado de "¿qué hospitalización activa corresponde a este dispositivo?", para la ingesta (fase 3).</summary>
/// <param name="EscalaSpo2">Escala de SpO2 con la que el motor clínico puntúa las lecturas de este dispositivo.</param>
public sealed record HospitalizacionPorDispositivoDto(
    Guid HospitalizacionId, Guid PacienteId, Guid CamaId, Guid DispositivoId, Guid AsignacionId, DateTime AsignadoEn,
    EscalaSpo2 EscalaSpo2 = EscalaSpo2.Escala1);
