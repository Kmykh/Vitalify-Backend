using Vitalify.Domain.Turnos;

namespace Vitalify.Application.Turnos;

/// <summary>La guardia (diurna o nocturna) que contiene un instante: entre las dos cubren las 24 h.</summary>
public static class Guardias
{
    public static (TurnoDelDia? Turno, IntervaloTurno Intervalo) Actual(CatalogoTurnos catalogo, DateTime ahora)
    {
        ArgumentNullException.ThrowIfNull(catalogo);
        var turno = catalogo.TurnosEn(ahora)
            .Where(t => t.Tipo is TipoTurno.GuardiaDiurna or TipoTurno.GuardiaNocturna)
            .Select(t => (TurnoDelDia?)t)
            .FirstOrDefault();
        return turno is { } t ? (t, catalogo.Intervalo(t)) : (null, new IntervaloTurno(ahora.AddHours(-12), ahora));
    }

    /// <summary>Por ejemplo <c>2026-11-09/GuardiaDiurna</c>.</summary>
    public static string? Clave(TurnoDelDia? turno) => turno is { } t ? $"{t.Fecha:yyyy-MM-dd}/{t.Tipo}" : null;
}
