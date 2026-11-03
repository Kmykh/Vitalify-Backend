using System.Globalization;
using Microsoft.Extensions.Configuration;
using Vitalify.Application.Alertas;
using Vitalify.Application.Clinica;
using Vitalify.Application.Telemetria;
using Vitalify.Application.Vinculacion;
using Vitalify.Domain.Comun;
using Vitalify.Domain.Alertas;
using Vitalify.Domain.Telemetria;
using Vitalify.Domain.Turnos;
using Vitalify.Infrastructure.Notificaciones;

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
    /// Sección <c>Clinico</c>: <c>Clinico__MinutosToleranciaToma</c> (30; reemplaza a <c>HorasVigenciaObservacion</c>) y
    /// <c>Clinico__AjusteTemperaturaSensor</c> (0, en °C; se suma a la temperatura del wearable antes de puntuar).
    /// </summary>
    public static OpcionesClinicas CargarOpcionesClinicas(IConfiguration configuration)
    {
        var s = configuration.GetSection("Clinico");
        var ajuste = Numero(s, "AjusteTemperaturaSensor", 0m);
        if (Math.Abs(ajuste) > 5m)
        {
            throw new InvalidOperationException("Clinico__AjusteTemperaturaSensor debe estar entre -5 y 5 °C.");
        }

        return new OpcionesClinicas(TimeSpan.FromMinutes((double)Numero(s, "MinutosToleranciaToma", 30, minimo: 0)), ajuste);
    }

    /// <summary>
    /// Sección <c>Vinculacion</c>: <c>Vinculacion__MinutosVigenciaCodigo</c> (10), <c>Vinculacion__MinutosVigenciaToken</c>
    /// (5), <c>Vinculacion__IntentosMaximos</c> (5) y <c>Vinculacion__SegundosRenovacion</c> (60; 0 apaga el worker).
    /// </summary>
    public static OpcionesVinculacion CargarOpcionesVinculacion(IConfiguration configuration)
    {
        var s = configuration.GetSection("Vinculacion");
        var d = OpcionesVinculacion.PorDefecto;
        return new OpcionesVinculacion(
            TimeSpan.FromMinutes((double)Numero(s, "MinutosVigenciaCodigo", (decimal)d.VigenciaCodigo.TotalMinutes, minimo: 1)),
            TimeSpan.FromMinutes((double)Numero(s, "MinutosVigenciaToken", (decimal)d.VigenciaToken.TotalMinutes, minimo: 1)),
            (int)Numero(s, "IntentosMaximos", d.IntentosMaximos, minimo: 1),
            TimeSpan.FromSeconds((double)Numero(s, "SegundosRenovacion", (decimal)d.IntervaloRenovacion.TotalSeconds, minimo: 0)));
    }

    /// <summary>
    /// Sección <c>Turnos</c>: <c>Turnos__ZonaHoraria</c> (<c>America/Lima</c>) y <c>Turnos__Tipos__{Tipo}__Inicio</c> /
    /// <c>__Fin</c> en <c>HH:mm</c> (por defecto GuardiaDiurna 07–19, GuardiaNocturna 19–07, Manana 07–13 y Tarde 13–19).
    /// </summary>
    public static CatalogoTurnos CargarCatalogoTurnos(IConfiguration configuration)
    {
        var s = configuration.GetSection("Turnos");
        var horarios = new Dictionary<TipoTurno, HorarioTurno>();
        foreach (var (tipo, porDefecto) in CatalogoTurnos.HorariosPorDefecto)
        {
            var seccion = s.GetSection("Tipos").GetSection(tipo.ToString());
            horarios[tipo] = new HorarioTurno(Hora(seccion, "Inicio", porDefecto.Inicio), Hora(seccion, "Fin", porDefecto.Fin));
        }

        try
        {
            return new CatalogoTurnos(horarios, Zona(string.IsNullOrWhiteSpace(s["ZonaHoraria"]) ? "America/Lima" : s["ZonaHoraria"]!.Trim()));
        }
        catch (ExcepcionDeDominio ex)
        {
            throw new InvalidOperationException($"Configuración Turnos__Tipos inválida: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Sección <c>Alertas</c>: <c>Alertas__SegundosSinSenal</c> (180), <c>Alertas__Escalamiento__MinutosAlta</c> (5),
    /// <c>Alertas__Escalamiento__MinutosMedia</c> (15), <c>Alertas__SegundosVigilancia</c> (30; 0 apaga el vigilante) y
    /// <c>Alertas__MinutosEsperaTrasAtender</c> (30; 0 la desactiva).
    /// </summary>
    public static OpcionesAlertas CargarOpcionesAlertas(IConfiguration configuration)
    {
        var s = configuration.GetSection("Alertas");
        var escalamiento = s.GetSection("Escalamiento");
        return new OpcionesAlertas(
            TimeSpan.FromSeconds((double)Numero(s, "SegundosSinSenal", 180, minimo: 10)),
            new ReglasEscalamiento(
                TimeSpan.FromMinutes((double)Numero(escalamiento, "MinutosAlta", 5, minimo: 0)),
                TimeSpan.FromMinutes((double)Numero(escalamiento, "MinutosMedia", 15, minimo: 0))),
            TimeSpan.FromSeconds((double)Numero(s, "SegundosVigilancia", 30, minimo: 0)))
        {
            EsperaTrasAtender = TimeSpan.FromMinutes((double)Numero(s, "MinutosEsperaTrasAtender", 30, minimo: 0)),
        };
    }

    /// <summary>
    /// Sección <c>Push</c>: <c>Push__Habilitado</c> (false) y <c>Push__RutaCredencialesFirebase</c>, que debe existir si se
    /// habilita. El JSON de Firebase nunca va al repositorio.
    /// </summary>
    public static OpcionesPush CargarOpcionesPush(IConfiguration configuration)
    {
        var s = configuration.GetSection("Push");
        var habilitado = bool.TryParse(s["Habilitado"], out var h) && h;
        var ruta = string.IsNullOrWhiteSpace(s["RutaCredencialesFirebase"]) ? null : s["RutaCredencialesFirebase"]!.Trim();
        if (habilitado && (ruta is null || !File.Exists(ruta)))
        {
            throw new InvalidOperationException("Push__Habilitado=true necesita Push__RutaCredencialesFirebase con la ruta al JSON de la cuenta de servicio de Firebase.");
        }

        return new OpcionesPush(habilitado, ruta);
    }

    private static TimeOnly Hora(IConfigurationSection seccion, string clave, TimeOnly porDefecto)
    {
        var texto = seccion[clave];
        if (string.IsNullOrWhiteSpace(texto))
        {
            return porDefecto;
        }

        return TimeOnly.TryParseExact(texto.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var hora)
            ? hora
            : throw new InvalidOperationException($"{seccion.Path.Replace(":", "__", StringComparison.Ordinal)}__{clave} debe tener el formato HH:mm.");
    }

    /// <summary>La zona del sistema (tzdata). Si falta y es Lima, UTC−5 fijo: Perú no tiene horario de verano.</summary>
    private static TimeZoneInfo Zona(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException) when (id == "America/Lima")
        {
            return TimeZoneInfo.CreateCustomTimeZone(id, TimeSpan.FromHours(-5), id, id);
        }
        catch (TimeZoneNotFoundException ex)
        {
            throw new InvalidOperationException($"Turnos__ZonaHoraria: la zona {id} no existe en este sistema.", ex);
        }
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
