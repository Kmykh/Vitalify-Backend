using Vitalify.Application.Comun;
using Vitalify.Application.Pacientes;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Historial;
using Vitalify.Domain.Hospitalizaciones;
using Vitalify.Domain.Pacientes;

namespace Vitalify.Application.Historial;

public static class ErroresHistorial
{
    public static readonly Error HospitalizacionNoEncontrada =
        Error.NoEncontrado("hospitalizacion-no-encontrada", "La hospitalización no existe o no es de este paciente.");

    public static readonly Error SinHospitalizaciones =
        Error.NoEncontrado("paciente-sin-hospitalizaciones", "El paciente no tiene hospitalizaciones registradas.");
}

/// <summary>Paciente y episodio sobre los que se consulta el historial.</summary>
public sealed record EpisodioElegido(Paciente Paciente, Hospitalizacion Hospitalizacion);

/// <summary>
/// Elige el episodio de una consulta del historial: el indicado en <c>hospitalizacionId</c> (debe ser del paciente) o,
/// por defecto, la hospitalización activa o, si no hay, la última. No filtra por servicio: el paciente pudo cambiar de
/// servicio y su historial lo puede leer cualquier médico o enfermera (queda auditado).
/// </summary>
public sealed class EpisodiosPaciente(IRepositorioPacientes pacientes, IRepositorioHospitalizaciones hospitalizaciones)
{
    public async Task<Resultado<EpisodioElegido>> ElegirAsync(Guid pacienteId, Guid? hospitalizacionId, CancellationToken ct = default)
    {
        var paciente = await pacientes.ObtenerPorIdAsync(pacienteId, ct);
        if (paciente is null)
        {
            return ErroresPaciente.NoEncontrado;
        }

        if (hospitalizacionId is { } id)
        {
            var indicada = await hospitalizaciones.ObtenerPorIdAsync(id, ct);
            return indicada is not null && indicada.PacienteId == pacienteId
                ? new EpisodioElegido(paciente, indicada)
                : ErroresHistorial.HospitalizacionNoEncontrada;
        }

        var ultima = await hospitalizaciones.ObtenerUltimaDePacienteAsync(pacienteId, ct);
        return ultima is null ? ErroresHistorial.SinHospitalizaciones : new EpisodioElegido(paciente, ultima);
    }

    /// <summary>Rango de la consulta dentro del episodio (ver <see cref="VentanaHistorial"/>), o el error de validación.</summary>
    public static Resultado<VentanaResuelta> Ventana(Hospitalizacion h, DateTime ahora, DateTime? desde, DateTime? hasta, bool agrupado)
    {
        var ventana = VentanaHistorial.Resolver(h.IngresoEn, h.EgresoEn, ahora, desde?.ToUniversalTime(), hasta?.ToUniversalTime(), agrupado);
        return ventana.EsValida ? ventana : ErroresComunes.ValidacionDeCampo(ventana.CampoInvalido!, ventana.Mensaje!);
    }
}

/// <summary>
/// Cada consulta del historial, del resumen, de las alertas o de las notas de un paciente queda en la auditoría como
/// <see cref="AccionAuditoria.ConsultaHistorialPaciente"/>, solo con ids (Ley 29733).
/// </summary>
public sealed class AuditoriaHistorial(IRepositorioAuditoria auditoria, IUnidadDeTrabajo unidadDeTrabajo, IReloj reloj)
{
    public async Task<Resultado<Unidad>> RegistrarAsync(
        Guid pacienteId, Guid? hospitalizacionId, string recurso, Guid usuarioId, string? ip, CancellationToken ct = default)
    {
        var detalle = hospitalizacionId is { } h
            ? $"Paciente {pacienteId}, hospitalización {h}: {recurso}."
            : $"Paciente {pacienteId}: {recurso}.";
        auditoria.Agregar(RegistroAuditoria.Registrar(AccionAuditoria.ConsultaHistorialPaciente, reloj.AhoraUtc, usuarioId, ip, detalle));
        return await unidadDeTrabajo.GuardarCambiosAsync(ct);
    }
}

/// <summary>Quién registró una toma o una nota (personal del hospital, no datos del paciente).</summary>
public sealed record ProfesionalDto(Guid Id, string Nombre, string Rol);

/// <summary>Nombres y roles del personal, para mostrar quién registró cada cosa.</summary>
public sealed class DirectorioProfesionales(IRepositorioUsuarios usuarios)
{
    public const string NombreNoDisponible = "(usuario no disponible)";

    public async Task<IReadOnlyDictionary<Guid, ProfesionalDto>> ObtenerAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var distintos = ids.Distinct().ToList();
        if (distintos.Count == 0)
        {
            return new Dictionary<Guid, ProfesionalDto>();
        }

        return (await usuarios.ObtenerVariosAsync(distintos, ct)).ToDictionary(u => u.Id, u => new ProfesionalDto(u.Id, u.Nombre, u.Rol.ToString()));
    }

    public static ProfesionalDto De(IReadOnlyDictionary<Guid, ProfesionalDto> directorio, Guid id) =>
        directorio.GetValueOrDefault(id) ?? new ProfesionalDto(id, NombreNoDisponible, string.Empty);
}

public sealed record CamaEpisodioDto(Guid Id, string Codigo);

public sealed record ServicioEpisodioDto(Guid Id, string Codigo, string Nombre);

public sealed record SensorEpisodioDto(Guid DispositivoId, string Codigo, DateTime AsignadoEn, DateTime? LiberadoEn, string? MotivoLiberacion);

/// <summary>Un episodio: ingreso, egreso y motivo, cama, servicio, diagnóstico y los sensores que se usaron.</summary>
/// <param name="Estado"><c>Activa</c> o <c>Finalizada</c>.</param>
public sealed record EpisodioDto(
    Guid HospitalizacionId, string Estado, DateTime IngresoEn, DateTime? EgresoEn, string? MotivoEgreso, string? ObservacionEgreso,
    string DiagnosticoIngreso, string EscalaSpo2, CamaEpisodioDto Cama, ServicioEpisodioDto Servicio, IReadOnlyList<SensorEpisodioDto> Sensores)
{
    public static EpisodioDto Desde(EpisodioLectura e, IEnumerable<SensorEpisodioLectura> sensores) =>
        new(e.HospitalizacionId, e.Estado.ToString(), e.IngresoEn, e.EgresoEn, e.MotivoEgreso?.ToString(), e.ObservacionEgreso,
            e.DiagnosticoIngreso, e.EscalaSpo2.ToString(), new CamaEpisodioDto(e.CamaId, e.CodigoCama),
            new ServicioEpisodioDto(e.ServicioId, e.CodigoServicio, e.NombreServicio),
            sensores.Where(s => s.HospitalizacionId == e.HospitalizacionId)
                .OrderBy(s => s.AsignadoEn)
                .Select(s => new SensorEpisodioDto(s.DispositivoId, s.CodigoDispositivo, s.AsignadoEn, s.LiberadoEn, s.MotivoLiberacion?.ToString()))
                .ToList());
}

public sealed record HospitalizacionesPacienteDto(Guid PacienteId, IReadOnlyList<EpisodioDto> Episodios);

public sealed record ListarHospitalizacionesConsulta(Guid PacienteId, Guid UsuarioId, string? Ip);

/// <summary>Todos los episodios del paciente, del más reciente al más antiguo (HU19).</summary>
public sealed class ListarHospitalizacionesPaciente(
    IRepositorioPacientes pacientes, IRepositorioHospitalizaciones hospitalizaciones, AuditoriaHistorial auditoria)
{
    public async Task<Resultado<HospitalizacionesPacienteDto>> EjecutarAsync(ListarHospitalizacionesConsulta consulta, CancellationToken ct = default)
    {
        if (await pacientes.ObtenerPorIdAsync(consulta.PacienteId, ct) is null)
        {
            return ErroresPaciente.NoEncontrado;
        }

        var episodios = await hospitalizaciones.ListarEpisodiosAsync(consulta.PacienteId, ct);
        var sensores = await hospitalizaciones.ListarSensoresAsync(episodios.Select(e => e.HospitalizacionId).ToList(), ct);
        var auditado = await auditoria.RegistrarAsync(consulta.PacienteId, null, "hospitalizaciones", consulta.UsuarioId, consulta.Ip, ct);
        return auditado.EsExito
            ? new HospitalizacionesPacienteDto(consulta.PacienteId, episodios.Select(e => EpisodioDto.Desde(e, sensores)).ToList())
            : auditado.Error;
    }
}
