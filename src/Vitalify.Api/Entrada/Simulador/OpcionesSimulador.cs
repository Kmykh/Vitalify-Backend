using System.Globalization;

namespace Vitalify.Api.Entrada.Simulador;

public enum EscenarioSimulacion
{
    /// <summary>Valores normales con ruido pequeño.</summary>
    Estable = 1,

    /// <summary>Empieza estable y empeora gradualmente (sube FC, FR y temperatura; baja SpO2 y presión).</summary>
    Deterioro = 2,

    /// <summary>Estable, con <c>caida=true</c> cada cierto número de lecturas.</summary>
    Caida = 3,

    /// <summary>Valores imposibles, presión sin diastólica y periodos sin temperatura.</summary>
    SensorDefectuoso = 4,
}

/// <summary>
/// Sección <c>Simulador</c> (variables <c>Simulador__*</c>). Apagado por defecto. El intervalo mínimo es 5 s para
/// no llenar el plan gratuito de Supabase (500 MB).
/// </summary>
public sealed record OpcionesSimulador
{
    public const string Seccion = "Simulador";
    public const int IntervaloMinimoSegundos = 5;

    public bool Habilitado { get; init; }
    public int IntervaloSegundos { get; init; } = 10;
    public int Semilla { get; init; } = 12345;
    public double MinutosDeterioro { get; init; } = 30;

    /// <summary>Con el escenario <see cref="EscenarioSimulacion.Caida"/>, una caída cada tantas lecturas.</summary>
    public int LecturasEntreCaidas { get; init; } = 18;

    /// <summary>Escenario por código de dispositivo (<c>Simulador__Escenarios__ESP32-001=Deterioro</c>).</summary>
    public IReadOnlyDictionary<string, EscenarioSimulacion> Escenarios { get; init; } = new Dictionary<string, EscenarioSimulacion>();

    public EscenarioSimulacion EscenarioDe(string codigoDispositivo) =>
        Escenarios.TryGetValue(codigoDispositivo, out var escenario) ? escenario : EscenarioSimulacion.Estable;

    public static OpcionesSimulador Cargar(IConfiguration configuration)
    {
        var s = configuration.GetSection(Seccion);
        var escenarios = new Dictionary<string, EscenarioSimulacion>(StringComparer.OrdinalIgnoreCase);
        foreach (var hijo in s.GetSection("Escenarios").GetChildren())
        {
            if (!Enum.TryParse<EscenarioSimulacion>(hijo.Value, ignoreCase: true, out var escenario) || !Enum.IsDefined(escenario))
            {
                throw new InvalidOperationException(
                    $"Simulador__Escenarios__{hijo.Key} debe ser uno de: {string.Join(", ", Enum.GetNames<EscenarioSimulacion>())}.");
            }

            escenarios[hijo.Key.Trim().ToUpperInvariant()] = escenario;
        }

        return new OpcionesSimulador
        {
            Habilitado = bool.TryParse(s["Habilitado"], out var habilitado) && habilitado,
            IntervaloSegundos = Entero(s, "IntervaloSegundos", 10),
            Semilla = Entero(s, "Semilla", 12345),
            MinutosDeterioro = Math.Max(1, Entero(s, "MinutosDeterioro", 30)),
            LecturasEntreCaidas = Math.Max(2, Entero(s, "LecturasEntreCaidas", 18)),
            Escenarios = escenarios,
        };
    }

    private static int Entero(IConfigurationSection seccion, string clave, int porDefecto) =>
        int.TryParse(seccion[clave], NumberStyles.Integer, CultureInfo.InvariantCulture, out var valor) ? valor : porDefecto;
}
