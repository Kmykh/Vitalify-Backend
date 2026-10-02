using Vitalify.Domain.Comun;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Domain.Clinica;

/// <summary>
/// Signos que registra el personal clínico porque el wearable no los mide (FR, presión, conciencia, oxígeno
/// suplementario) o para confirmar la temperatura con un termómetro clínico. Vive en el historial, como las lecturas.
/// </summary>
public sealed class ObservacionEnfermeria
{
    public Guid Id { get; private set; }
    public Guid HospitalizacionId { get; private set; }
    public Guid PacienteId { get; private set; }

    /// <summary>Momento en que se tomó la medición.</summary>
    public DateTime ObservadaEn { get; private set; }

    public DateTime RegistradaEn { get; private set; }
    public Guid RegistradaPor { get; private set; }
    public int? Fr { get; private set; }
    public int? Pas { get; private set; }
    public int? Pad { get; private set; }
    public decimal? Temperatura { get; private set; }
    public NivelConciencia? Conciencia { get; private set; }
    public bool? OxigenoSuplementario { get; private set; }

    private ObservacionEnfermeria()
    {
    }

    public static ObservacionEnfermeria Registrar(
        Guid hospitalizacionId, Guid pacienteId, DateTime observadaEn, DateTime registradaEn, Guid registradaPor,
        int? fr, int? pas, int? pad, decimal? temperatura, NivelConciencia? conciencia, bool? oxigenoSuplementario,
        RangosFisiologicos rangos)
    {
        ArgumentNullException.ThrowIfNull(rangos);
        if (fr is null && pas is null && pad is null && temperatura is null && conciencia is null && oxigenoSuplementario is null)
        {
            throw new ExcepcionDeDominio("La observación debe tener al menos un valor.");
        }

        if (observadaEn > registradaEn)
        {
            throw new ExcepcionDeDominio("La observación no puede ser posterior a su registro.");
        }

        if ((pas is null) != (pad is null) || (pas is not null && pas <= pad))
        {
            throw new ExcepcionDeDominio("La presión arterial necesita la sistólica y la diastólica, con la sistólica mayor.");
        }

        Validar(VariableSigno.Fr, fr, rangos);
        Validar(VariableSigno.Pas, pas, rangos);
        Validar(VariableSigno.Pad, pad, rangos);
        Validar(VariableSigno.Temperatura, temperatura, rangos);
        if (conciencia is { } c && !Enum.IsDefined(c))
        {
            throw new ExcepcionDeDominio("El nivel de conciencia no es válido.");
        }

        return new ObservacionEnfermeria
        {
            Id = Guid.NewGuid(),
            HospitalizacionId = hospitalizacionId,
            PacienteId = pacienteId,
            ObservadaEn = observadaEn,
            RegistradaEn = registradaEn,
            RegistradaPor = registradaPor,
            Fr = fr,
            Pas = pas,
            Pad = pad,
            Temperatura = temperatura is { } t ? Math.Round(t, 1, MidpointRounding.AwayFromZero) : null,
            Conciencia = conciencia,
            OxigenoSuplementario = oxigenoSuplementario,
        };
    }

    private static void Validar(VariableSigno variable, decimal? valor, RangosFisiologicos rangos)
    {
        if (valor is { } v && !rangos.EsValido(variable, v))
        {
            throw new ExcepcionDeDominio($"{variable} fuera del rango posible ({rangos.De(variable)}).");
        }
    }
}
