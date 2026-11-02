using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Application.Servicios;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Turnos;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Application.Turnos;

/// <summary>Un día de la planilla tal como llega: por nombre de turno, las letras.</summary>
public sealed record DiaPlanillaComando(int Dia, IReadOnlyDictionary<string, IReadOnlyList<string>> Turnos);

public sealed record CargarProgramacionComando(
    Guid ServicioId, int Anio, int Mes, IReadOnlyDictionary<string, Guid>? Letras, IReadOnlyList<DiaPlanillaComando>? Dias,
    Guid AdministradorId, string? Ip);

public sealed record EnfermeraPlanillaDto(string Letra, Guid UsuarioId, string Nombre);

public sealed record DiaProgramacionDto(int Dia, IReadOnlyDictionary<string, IReadOnlyList<string>> Turnos);

/// <summary>La planilla del mes con el mismo formato del papel, más los nombres de las enfermeras.</summary>
public sealed record ProgramacionMesDto(
    Guid ServicioId, int Anio, int Mes, IReadOnlyList<EnfermeraPlanillaDto> Letras, IReadOnlyList<DiaProgramacionDto> Dias, int Turnos);

/// <summary>
/// Carga la programación mensual de un servicio con el formato de la planilla de papel. Reemplaza el mes completo.
/// Cada letra debe ser una enfermera activa del servicio; los errores salen por día y letra.
/// </summary>
public sealed class CargarProgramacion(
    IRepositorioServicios servicios, IRepositorioUsuarios usuarios, IRepositorioProgramacion programacion, IRepositorioAuditoria auditoria,
    IUnidadDeTrabajo unidadDeTrabajo, ObtenerProgramacion obtener, CatalogoTurnos catalogo, IReloj reloj)
{
    public async Task<Resultado<ProgramacionMesDto>> EjecutarAsync(CargarProgramacionComando comando, CancellationToken ct = default)
    {
        if (await servicios.ObtenerPorIdAsync(comando.ServicioId, ct) is null)
        {
            return ErroresServicio.NoEncontrado;
        }

        var errores = new Dictionary<string, string[]>();
        if (comando.Letras is not { Count: > 0 })
        {
            errores["letras"] = ["Indica al menos una letra con su enfermera."];
        }

        if (comando.Dias is null)
        {
            errores["dias"] = ["Indica los días de la planilla."];
        }

        if (errores.Count > 0)
        {
            return Error.Validacion(errores);
        }

        var letras = comando.Letras!.ToDictionary(l => PlanillaMensual.Normalizar(l.Key), l => l.Value);
        var enfermeras = (await usuarios.ObtenerVariosAsync(letras.Values.Distinct().ToList(), ct)).ToDictionary(u => u.Id);
        foreach (var (letra, usuarioId) in letras)
        {
            if (letra.Length is 0 or > ProgramacionTurno.LargoMaximoLetra || !letra.All(char.IsAsciiLetterOrDigit))
            {
                errores[$"letras.{letra}"] = ["La letra debe tener de 1 a 3 letras o números."];
            }
            else if (!enfermeras.TryGetValue(usuarioId, out var usuario) || usuario.Rol != Rol.Enfermera || !usuario.Activo
                     || usuario.ServicioId != comando.ServicioId)
            {
                errores[$"letras.{letra}"] = [$"La letra {letra} debe ser una enfermera activa del servicio."];
            }
        }

        var dias = new List<DiaPlanilla>();
        foreach (var dia in comando.Dias!)
        {
            var turnos = new Dictionary<TipoTurno, IReadOnlyList<string>>();
            foreach (var (nombre, letrasTurno) in dia.Turnos)
            {
                if (Enumeraciones.TryParseNombre<TipoTurno>(nombre, out var tipo))
                {
                    turnos[tipo] = letrasTurno;
                }
                else
                {
                    errores[$"dias.{dia.Dia}.{nombre}"] = [$"El turno {nombre} no existe: {Enumeraciones.Nombres<TipoTurno>()}."];
                }
            }

            dias.Add(new DiaPlanilla(dia.Dia, turnos));
        }

        var resultado = PlanillaMensual.Validar(comando.Anio, comando.Mes, letras, dias, catalogo);
        foreach (var (clave, mensajes) in resultado.Errores)
        {
            errores[clave] = errores.TryGetValue(clave, out var previos) ? [.. previos, .. mensajes] : mensajes;
        }

        if (errores.Count > 0)
        {
            return Error.Validacion(errores);
        }

        var ahora = reloj.AhoraUtc;
        var nuevos = resultado.Asignaciones
            .Select(a => ProgramacionTurno.Crear(comando.ServicioId, a.Turno, a.UsuarioId, a.Letra, comando.AdministradorId, ahora))
            .ToList();
        await programacion.ReemplazarMesAsync(comando.ServicioId, comando.Anio, comando.Mes, nuevos, ct);
        auditoria.Agregar(RegistroAuditoria.Registrar(
            AccionAuditoria.ProgramacionCargada, ahora, comando.AdministradorId, comando.Ip,
            $"Servicio {comando.ServicioId}, {comando.Mes:00}/{comando.Anio}: {nuevos.Count} turnos."));

        var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return guardado.EsExito ? await obtener.EjecutarAsync(comando.ServicioId, comando.Anio, comando.Mes, ct) : guardado.Error;
    }
}

/// <summary>La planilla del mes de un servicio, con letras y nombres.</summary>
public sealed class ObtenerProgramacion(IRepositorioServicios servicios, IRepositorioUsuarios usuarios, IRepositorioProgramacion programacion)
{
    public async Task<Resultado<ProgramacionMesDto>> EjecutarAsync(Guid servicioId, int anio, int mes, CancellationToken ct = default)
    {
        if (await servicios.ObtenerPorIdAsync(servicioId, ct) is null)
        {
            return ErroresServicio.NoEncontrado;
        }

        if (mes is < 1 or > 12 || anio is < 2000 or > 2100)
        {
            return ErroresComunes.ValidacionDeCampo("mes", "El año o el mes no son válidos.");
        }

        var primero = new DateOnly(anio, mes, 1);
        var turnos = await programacion.ListarAsync(servicioId, primero, primero.AddMonths(1).AddDays(-1), ct);
        var nombres = (await usuarios.ObtenerVariosAsync(turnos.Select(t => t.UsuarioId).Distinct().ToList(), ct)).ToDictionary(u => u.Id, u => u.Nombre);

        var letras = turnos
            .GroupBy(t => t.Letra)
            .Select(g => new EnfermeraPlanillaDto(g.Key, g.First().UsuarioId, nombres.GetValueOrDefault(g.First().UsuarioId, string.Empty)))
            .OrderBy(l => l.Letra, StringComparer.Ordinal)
            .ToList();
        var dias = turnos
            .GroupBy(t => t.Fecha.Day)
            .OrderBy(g => g.Key)
            .Select(g => new DiaProgramacionDto(g.Key, g
                .GroupBy(t => t.TipoTurno)
                .OrderBy(t => t.Key)
                .ToDictionary(t => t.Key.ToString(), t => (IReadOnlyList<string>)t.Select(x => x.Letra).Order(StringComparer.Ordinal).ToList())))
            .ToList();

        return new ProgramacionMesDto(servicioId, anio, mes, letras, dias, turnos.Count);
    }
}

public sealed record EnfermeraDeTurnoDto(Guid UsuarioId, string Nombre, string? Letra, string? Turno, DateTime? InicioEn, DateTime? FinEn);

/// <param name="EsRespaldo">No había programación: son todas las enfermeras activas del servicio.</param>
public sealed record TurnoActualDto(Guid ServicioId, DateTime Ahora, IReadOnlyList<EnfermeraDeTurnoDto> Enfermeras, bool EsRespaldo);

/// <summary>Quién está de turno ahora en el servicio (por defecto, el del usuario).</summary>
public sealed class ObtenerTurnoActual(
    IRepositorioServicios servicios, IRepositorioUsuarios usuarios, IRepositorioProgramacion programacion, ITurnoVigente turnoVigente,
    CatalogoTurnos catalogo, IReloj reloj)
{
    public async Task<Resultado<TurnoActualDto>> EjecutarAsync(Guid? servicioId, Guid usuarioId, CancellationToken ct = default)
    {
        var id = servicioId ?? (await usuarios.ObtenerPorIdAsync(usuarioId, ct))?.ServicioId;
        if (id is not { } servicio)
        {
            return ErroresComunes.ValidacionDeCampo("servicioId", "Indica el servicio: tu cuenta no tiene uno asignado.");
        }

        if (await servicios.ObtenerPorIdAsync(servicio, ct) is null)
        {
            return ErroresServicio.NoEncontrado;
        }

        var ahora = reloj.AhoraUtc;
        var vigentes = catalogo.TurnosEn(ahora);
        var programados = (await programacion.ListarAsync(servicio, vigentes.Min(t => t.Fecha), vigentes.Max(t => t.Fecha), ct))
            .Where(p => vigentes.Contains(p.Turno))
            .ToList();

        if (programados.Count > 0)
        {
            var nombres = (await usuarios.ObtenerVariosAsync(programados.Select(p => p.UsuarioId).Distinct().ToList(), ct)).ToDictionary(u => u.Id, u => u.Nombre);
            return new TurnoActualDto(servicio, ahora, programados
                .OrderBy(p => p.TipoTurno).ThenBy(p => p.Letra, StringComparer.Ordinal)
                .Select(p =>
                {
                    var intervalo = catalogo.Intervalo(p.Turno);
                    return new EnfermeraDeTurnoDto(p.UsuarioId, nombres.GetValueOrDefault(p.UsuarioId, string.Empty), p.Letra, p.TipoTurno.ToString(),
                        intervalo.InicioUtc, intervalo.FinUtc);
                })
                .ToList(), EsRespaldo: false);
        }

        var respaldo = await turnoVigente.EnfermerasDeTurnoAsync(servicio, ahora, ct);
        var enfermeras = await usuarios.ObtenerVariosAsync(respaldo.Enfermeras, ct);
        return new TurnoActualDto(servicio, ahora, enfermeras
            .OrderBy(u => u.Nombre, StringComparer.Ordinal)
            .Select(u => new EnfermeraDeTurnoDto(u.Id, u.Nombre, null, null, null, null))
            .ToList(), EsRespaldo: true);
    }
}

public sealed record MiTurnoDto(Guid ServicioId, DateOnly Fecha, string Turno, string Letra, DateTime InicioEn, DateTime FinEn);

/// <summary>Los turnos programados de quien consulta (por defecto, de hoy a 30 días; como máximo 62 días).</summary>
public sealed class ListarMisTurnos(IRepositorioProgramacion programacion, CatalogoTurnos catalogo, IReloj reloj)
{
    public const int DiasMaximos = 62;

    public async Task<Resultado<IReadOnlyList<MiTurnoDto>>> EjecutarAsync(Guid usuarioId, DateOnly? desde, DateOnly? hasta, CancellationToken ct = default)
    {
        var inicio = desde ?? catalogo.FechaLocal(reloj.AhoraUtc);
        var fin = hasta ?? inicio.AddDays(30);
        if (fin < inicio || fin.DayNumber - inicio.DayNumber > DiasMaximos)
        {
            return ErroresComunes.ValidacionDeCampo("hasta", $"El rango debe ir hacia adelante y no superar {DiasMaximos} días.");
        }

        var turnos = await programacion.ListarDeUsuarioAsync(usuarioId, inicio, fin, ct);
        return Resultado<IReadOnlyList<MiTurnoDto>>.Exito(turnos
            .OrderBy(t => t.Fecha).ThenBy(t => catalogo.Intervalo(t.Turno).InicioUtc)
            .Select(t =>
            {
                var intervalo = catalogo.Intervalo(t.Turno);
                return new MiTurnoDto(t.ServicioId, t.Fecha, t.TipoTurno.ToString(), t.Letra, intervalo.InicioUtc, intervalo.FinUtc);
            })
            .ToList());
    }
}
