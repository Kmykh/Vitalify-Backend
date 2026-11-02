using System.Globalization;
using Microsoft.Extensions.Configuration;
using Vitalify.Application.Clinica;
using Vitalify.Application.Telemetria;
using Vitalify.Domain.Comun;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Infrastructure.Telemetria;

/// <summary>
/// Lee la sección <c>Telemetria</c> (variables <c>Telemetria__*</c>). Todo tiene valor por defecto:
/// <c>SegundosVigencia</c> 90, <c>HorasMaximasRetraso</c> 24, <c>MinutosToleranciaFuturo</c> 2,
/// <c>SegundosCacheDispositivo</c> 15 y los rangos de <see cref="RangosFisiologicos.PorDefecto"/>
/// (<c>Telemetria__Rangos__Fc__Minimo</c>, <c>Telemetria__Rangos__Fc__Maximo</c>, etc.).
/// </summary>
internal static class ConfiguracionTelemetria
{
    public const string Seccion = "Telemetria";

    public static OpcionesTelemetria CargarOpciones(IConfiguration configuration)
    {
        var s = configuration.GetSection(Seccion);
        var cache = Numero(s, "SegundosCacheDispositivo", 15, minimo: 0);
        return new OpcionesTelemetria(
            TimeSpan.FromSeconds((double)Numero(s, "SegundosVigencia", 90, minimo: 1)),
            new VentanaDeRecepcion(
                TimeSpan.FromMinutes((double)Numero(s, "MinutosToleranciaFuturo", 2, minimo: 0)),
                TimeSpan.FromHours((double)Numero(s, "HorasMaximasRetraso", 24, minimo: 1))),
            TimeSpan.FromSeconds((double)cache));
    }

    /// <summary>
    /// Sección <c>Clinico</c>: <c>Clinico__HorasVigenciaObservacion</c> (4) y <c>Clinico__AjusteTemperaturaSensor</c>
    /// (0, en °C; se suma a la temperatura del wearable antes de puntuar).
    /// </summary>
    public static OpcionesClinicas CargarOpcionesClinicas(IConfiguration configuration)
    {
        var s = configuration.GetSection("Clinico");
        var ajuste = Numero(s, "AjusteTemperaturaSensor", 0m);
        if (Math.Abs(ajuste) > 5m)
        {
            throw new InvalidOperationException("Clinico__AjusteTemperaturaSensor debe estar entre -5 y 5 °C.");
        }

        return new OpcionesClinicas(TimeSpan.FromHours((double)Numero(s, "HorasVigenciaObservacion", 4, minimo: 0.25m)), ajuste);
    }

    public static RangosFisiologicos CargarRangos(IConfiguration configuration)
    {
        var rangos = configuration.GetSection(Seccion).GetSection("Rangos");
        var d = RangosFisiologicos.PorDefecto;

        RangoFisiologico Rango(string nombre, RangoFisiologico porDefecto)
        {
            var seccion = rangos.GetSection(nombre);
            return new RangoFisiologico(Numero(seccion, "Minimo", porDefecto.Minimo), Numero(seccion, "Maximo", porDefecto.Maximo));
        }

        try
        {
            return new RangosFisiologicos(
                Rango("Fc", d.Fc), Rango("Fr", d.Fr), Rango("Spo2", d.Spo2),
                Rango("Temperatura", d.Temperatura), Rango("Pas", d.Pas), Rango("Pad", d.Pad));
        }
        catch (ExcepcionDeDominio ex)
        {
            throw new InvalidOperationException($"Configuración Telemetria__Rangos inválida: {ex.Message}", ex);
        }
    }

    private static decimal Numero(IConfigurationSection seccion, string clave, decimal porDefecto, decimal? minimo = null)
    {
        var texto = seccion[clave];
        if (string.IsNullOrWhiteSpace(texto))
        {
            return porDefecto;
        }

        if (!decimal.TryParse(texto, NumberStyles.Number, CultureInfo.InvariantCulture, out var valor) || (minimo is { } m && valor < m))
        {
            throw new InvalidOperationException($"{seccion.Path.Replace(":", "__", StringComparison.Ordinal)}__{clave} no es un número válido.");
        }

        return valor;
    }
}
