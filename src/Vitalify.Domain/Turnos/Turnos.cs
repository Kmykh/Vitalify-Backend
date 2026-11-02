using Vitalify.Domain.Comun;

namespace Vitalify.Domain.Turnos;

/// <summary>Turnos de la programación de enfermería (Hospital II de EsSalud: guardias de 12 h y turnos de 6 h).</summary>
public enum TipoTurno
{
    /// <summary>07:00–19:00.</summary>
    GuardiaDiurna = 1,

    /// <summary>19:00–07:00 del día siguiente.</summary>
    GuardiaNocturna = 2,

    /// <summary>07:00–13:00.</summary>
    Manana = 3,

    /// <summary>13:00–19:00.</summary>
    Tarde = 4,
}

/// <summary>Horario de un turno en hora local del hospital. Si <see cref="Fin"/> no es mayor que <see cref="Inicio"/>, termina al día siguiente.</summary>
public readonly record struct HorarioTurno(TimeOnly Inicio, TimeOnly Fin)
{
    public bool CruzaMedianoche => Fin <= Inicio;
}

/// <summary>Instante de inicio (incluido) y de fin (excluido) de un turno, en UTC.</summary>
public readonly record struct IntervaloTurno(DateTime InicioUtc, DateTime FinUtc)
{
    public bool Contiene(DateTime instanteUtc) => instanteUtc >= InicioUtc && instanteUtc < FinUtc;

    /// <summary>Dos turnos se cruzan si se solapan; si uno empieza justo cuando termina el otro (07:00), no.</summary>
    public bool SeCruzaCon(IntervaloTurno otro) => InicioUtc < otro.FinUtc && otro.InicioUtc < FinUtc;
}

/// <summary>Un turno concreto: el día en que <b>empieza</b> (hora local) y su tipo.</summary>
public readonly record struct TurnoDelDia(DateOnly Fecha, TipoTurno Tipo);

/// <summary>
/// Horarios de los turnos y zona horaria del hospital (<c>America/Lima</c>). En la base todo está en UTC; la fecha de
/// la programación es la del día local en que empieza el turno.
/// </summary>
public sealed class CatalogoTurnos
{
    public static readonly IReadOnlyDictionary<TipoTurno, HorarioTurno> HorariosPorDefecto = new Dictionary<TipoTurno, HorarioTurno>
    {
        [TipoTurno.GuardiaDiurna] = new(new TimeOnly(7, 0), new TimeOnly(19, 0)),
        [TipoTurno.GuardiaNocturna] = new(new TimeOnly(19, 0), new TimeOnly(7, 0)),
        [TipoTurno.Manana] = new(new TimeOnly(7, 0), new TimeOnly(13, 0)),
        [TipoTurno.Tarde] = new(new TimeOnly(13, 0), new TimeOnly(19, 0)),
    };

    private readonly IReadOnlyDictionary<TipoTurno, HorarioTurno> _horarios;

    public CatalogoTurnos(IReadOnlyDictionary<TipoTurno, HorarioTurno> horarios, TimeZoneInfo zona)
    {
        ArgumentNullException.ThrowIfNull(horarios);
        ArgumentNullException.ThrowIfNull(zona);
        foreach (var tipo in Enum.GetValues<TipoTurno>())
        {
            if (!horarios.TryGetValue(tipo, out var horario))
            {
                throw new ExcepcionDeDominio($"Falta el horario del turno {tipo}.");
            }

            if (horario.Inicio == horario.Fin)
            {
                throw new ExcepcionDeDominio($"El turno {tipo} no puede empezar y terminar a la misma hora.");
            }
        }

        _horarios = horarios;
        Zona = zona;
    }

    public TimeZoneInfo Zona { get; }

    public HorarioTurno Horario(TipoTurno tipo) => _horarios[tipo];

    public IntervaloTurno Intervalo(TurnoDelDia turno)
    {
        var horario = _horarios[turno.Tipo];
        var inicio = turno.Fecha.ToDateTime(horario.Inicio, DateTimeKind.Unspecified);
        var fin = (horario.CruzaMedianoche ? turno.Fecha.AddDays(1) : turno.Fecha).ToDateTime(horario.Fin, DateTimeKind.Unspecified);
        return new IntervaloTurno(TimeZoneInfo.ConvertTimeToUtc(inicio, Zona), TimeZoneInfo.ConvertTimeToUtc(fin, Zona));
    }

    /// <summary>Día local del hospital en ese instante.</summary>
    public DateOnly FechaLocal(DateTime instanteUtc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(instanteUtc, DateTimeKind.Utc), Zona));

    /// <summary>Turnos (de cualquier tipo) que abarcan ese instante: los del día local y la nocturna del día anterior.</summary>
    public IReadOnlyList<TurnoDelDia> TurnosEn(DateTime instanteUtc)
    {
        var hoy = FechaLocal(instanteUtc);
        return new[] { hoy.AddDays(-1), hoy }
            .SelectMany(fecha => Enum.GetValues<TipoTurno>().Select(tipo => new TurnoDelDia(fecha, tipo)))
            .Where(t => Intervalo(t).Contiene(instanteUtc))
            .ToList();
    }

    /// <summary>Turnos que empiezan en (<paramref name="desdeUtc"/>, <paramref name="hastaUtc"/>]: sirven para avisar del cambio de turno.</summary>
    public IReadOnlyList<TurnoDelDia> TurnosQueEmpiezanEntre(DateTime desdeUtc, DateTime hastaUtc)
    {
        var desde = FechaLocal(desdeUtc).AddDays(-1);
        var hasta = FechaLocal(hastaUtc);
        var turnos = new List<TurnoDelDia>();
        for (var fecha = desde; fecha <= hasta; fecha = fecha.AddDays(1))
        {
            turnos.AddRange(Enum.GetValues<TipoTurno>()
                .Select(tipo => new TurnoDelDia(fecha, tipo))
                .Where(t => Intervalo(t).InicioUtc > desdeUtc && Intervalo(t).InicioUtc <= hastaUtc));
        }

        return turnos;
    }
}

/// <summary>Quién cubre un turno de un servicio, con la letra de la planilla.</summary>
public sealed class ProgramacionTurno
{
    public const int LargoMaximoLetra = 3;

    public Guid Id { get; private set; }
    public Guid ServicioId { get; private set; }

    /// <summary>Día local en que empieza el turno.</summary>
    public DateOnly Fecha { get; private set; }

    public TipoTurno TipoTurno { get; private set; }
    public Guid UsuarioId { get; private set; }
    public string Letra { get; private set; } = null!;
    public Guid CargadoPor { get; private set; }
    public DateTime CargadoEn { get; private set; }

    private ProgramacionTurno()
    {
    }

    public TurnoDelDia Turno => new(Fecha, TipoTurno);

    public static ProgramacionTurno Crear(Guid servicioId, TurnoDelDia turno, Guid usuarioId, string letra, Guid cargadoPor, DateTime cargadoEn) =>
        new()
        {
            Id = Guid.NewGuid(),
            ServicioId = servicioId,
            Fecha = turno.Fecha,
            TipoTurno = turno.Tipo,
            UsuarioId = usuarioId,
            Letra = letra,
            CargadoPor = cargadoPor,
            CargadoEn = cargadoEn,
        };
}

/// <summary>Un día de la planilla: por cada turno, las letras que lo cubren.</summary>
public sealed record DiaPlanilla(int Dia, IReadOnlyDictionary<TipoTurno, IReadOnlyList<string>> Turnos);

/// <summary>Un turno asignado por la planilla (todavía sin guardar).</summary>
public sealed record AsignacionPlanilla(TurnoDelDia Turno, string Letra, Guid UsuarioId);

/// <summary>Resultado de validar una planilla: las asignaciones o los errores por día y letra.</summary>
public sealed record ResultadoPlanilla(IReadOnlyList<AsignacionPlanilla> Asignaciones, IReadOnlyDictionary<string, string[]> Errores)
{
    public bool EsValida => Errores.Count == 0;
}

/// <summary>
/// Planilla mensual de un servicio, con el formato del papel: cada enfermera tiene una letra y cada día dice qué letra
/// cubre cada turno. Reglas: los días existen en el mes, cada letra usada está definida y una misma enfermera no tiene
/// dos turnos que se crucen (la nocturna del día 5 y la diurna del día 6 sí: se tocan a las 07:00).
/// </summary>
public static class PlanillaMensual
{
    public static ResultadoPlanilla Validar(
        int anio, int mes, IReadOnlyDictionary<string, Guid> letras, IReadOnlyList<DiaPlanilla> dias, CatalogoTurnos catalogo)
    {
        ArgumentNullException.ThrowIfNull(letras);
        ArgumentNullException.ThrowIfNull(dias);
        ArgumentNullException.ThrowIfNull(catalogo);

        var errores = new Dictionary<string, List<string>>();
        void Error(string clave, string mensaje)
        {
            if (!errores.TryGetValue(clave, out var lista))
            {
                errores[clave] = lista = [];
            }

            lista.Add(mensaje);
        }

        if (mes is < 1 or > 12 || anio is < 2000 or > 2100)
        {
            Error("mes", "El año o el mes no son válidos.");
            return Resultado([], errores);
        }

        var diasDelMes = DateTime.DaysInMonth(anio, mes);
        var asignaciones = new List<AsignacionPlanilla>();
        foreach (var grupo in dias.GroupBy(d => d.Dia).Where(g => g.Count() > 1))
        {
            Error($"dias.{grupo.Key}", $"El día {grupo.Key} aparece más de una vez.");
        }

        foreach (var dia in dias)
        {
            if (dia.Dia < 1 || dia.Dia > diasDelMes)
            {
                Error($"dias.{dia.Dia}", $"El día {dia.Dia} no existe en {mes:00}/{anio}.");
                continue;
            }

            var fecha = new DateOnly(anio, mes, dia.Dia);
            foreach (var (tipo, letrasDelTurno) in dia.Turnos)
            {
                foreach (var letra in letrasDelTurno.Select(Normalizar).Distinct())
                {
                    if (!letras.TryGetValue(letra, out var usuarioId))
                    {
                        Error($"dias.{dia.Dia}.{letra}", $"La letra {letra} del día {dia.Dia} ({tipo}) no está en letras.");
                        continue;
                    }

                    asignaciones.Add(new AsignacionPlanilla(new TurnoDelDia(fecha, tipo), letra, usuarioId));
                }
            }
        }

        // Una enfermera no puede estar en dos turnos que se solapan (aunque tenga dos letras).
        foreach (var porEnfermera in asignaciones.GroupBy(a => a.UsuarioId))
        {
            var ordenadas = porEnfermera.Select(a => (Asignacion: a, Intervalo: catalogo.Intervalo(a.Turno))).OrderBy(x => x.Intervalo.InicioUtc).ToList();
            for (var i = 1; i < ordenadas.Count; i++)
            {
                var (anterior, actual) = (ordenadas[i - 1], ordenadas[i]);
                if (actual.Intervalo.SeCruzaCon(anterior.Intervalo))
                {
                    Error($"dias.{actual.Asignacion.Turno.Fecha.Day}.{actual.Asignacion.Letra}",
                        $"La letra {actual.Asignacion.Letra} tiene {anterior.Asignacion.Turno.Tipo} del día {anterior.Asignacion.Turno.Fecha.Day} " +
                        $"y {actual.Asignacion.Turno.Tipo} del día {actual.Asignacion.Turno.Fecha.Day}, que se cruzan.");
                }
            }
        }

        return Resultado(asignaciones, errores);
    }

    public static string Normalizar(string letra) => (letra ?? string.Empty).Trim().ToUpperInvariant();

    private static ResultadoPlanilla Resultado(List<AsignacionPlanilla> asignaciones, Dictionary<string, List<string>> errores) =>
        new(errores.Count == 0 ? asignaciones : [], errores.ToDictionary(e => e.Key, e => e.Value.ToArray()));
}
