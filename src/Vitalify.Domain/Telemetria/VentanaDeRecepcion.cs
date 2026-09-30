using static System.FormattableString;
namespace Vitalify.Domain.Telemetria;

/// <summary>
/// Qué marcas de tiempo se aceptan: como máximo <see cref="ToleranciaFuturo"/> en el futuro (desfase de reloj del
/// ESP32) y como máximo <see cref="RetrasoMaximo"/> en el pasado (lecturas del búfer offline del dispositivo).
/// </summary>
public sealed record VentanaDeRecepcion(TimeSpan ToleranciaFuturo, TimeSpan RetrasoMaximo)
{
    public static readonly VentanaDeRecepcion PorDefecto = new(TimeSpan.FromMinutes(2), TimeSpan.FromHours(24));

    /// <returns>null si la marca es aceptable; si no, el motivo del rechazo.</returns>
    public string? Validar(DateTime medidoEn, DateTime ahora)
    {
        if (medidoEn > ahora + ToleranciaFuturo)
        {
            return Invariant($"La marca de tiempo {medidoEn:O} está más de {ToleranciaFuturo.TotalMinutes:0} minutos en el futuro.");
        }

        if (medidoEn < ahora - RetrasoMaximo)
        {
            return Invariant($"La marca de tiempo {medidoEn:O} tiene más de {RetrasoMaximo.TotalHours:0} horas de retraso.");
        }

        return null;
    }
}

public static class Vigencia
{
    /// <summary>
    /// Vigente si se midió hace <paramref name="vigencia"/> o menos; si es más antigua, queda pendiente de
    /// actualización (se conserva el último valor válido).
    /// </summary>
    public static EstadoVariable EstadoDe(DateTime? medidoEn, DateTime ahora, TimeSpan vigencia) =>
        medidoEn is not { } m ? EstadoVariable.SinDatos
        : m < ahora - vigencia ? EstadoVariable.PendienteActualizacion
        : EstadoVariable.Vigente;

    /// <summary>Sin señal si no llega ninguna lectura en el doble de la vigencia.</summary>
    public static EstadoSenal SenalDe(DateTime? ultimaLecturaEn, DateTime ahora, TimeSpan vigencia) =>
        ultimaLecturaEn is not { } u ? EstadoSenal.SinDatos
        : u < ahora - (vigencia * 2) ? EstadoSenal.SinSenal
        : EstadoSenal.ConDatos;
}
