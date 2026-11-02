using static System.FormattableString;
using Vitalify.Domain.Comun;

namespace Vitalify.Domain.Telemetria;

/// <summary>Intervalo cerrado [<see cref="Minimo"/>, <see cref="Maximo"/>].</summary>
public readonly record struct RangoFisiologico(decimal Minimo, decimal Maximo)
{
    public bool Contiene(decimal valor) => valor >= Minimo && valor <= Maximo;

    public override string ToString() => Invariant($"{Minimo} a {Maximo}");
}

/// <summary>
/// Rangos de <b>plausibilidad fisiológica</b>: un valor fuera de ellos no es un paciente grave, es un error del
/// sensor. No son rangos clínicos (esos llegan con NEWS2/MEWS en la fase 4).
/// </summary>
public sealed class RangosFisiologicos
{
    public static readonly RangosFisiologicos PorDefecto = new(
        fc: new(20, 250),
        fr: new(4, 60),
        spo2: new(50, 100),
        temperatura: new(30.0m, 43.0m),
        pas: new(50, 260),
        pad: new(20, 160));

    /// <summary>La batería no es un signo vital, pero también se valida.</summary>
    public static readonly RangoFisiologico Bateria = new(0, 100);

    public RangosFisiologicos(
        RangoFisiologico fc, RangoFisiologico fr, RangoFisiologico spo2,
        RangoFisiologico temperatura, RangoFisiologico pas, RangoFisiologico pad)
    {
        foreach (var (nombre, rango) in new[] { ("FC", fc), ("FR", fr), ("SpO2", spo2), ("Temperatura", temperatura), ("PAS", pas), ("PAD", pad) })
        {
            if (rango.Minimo >= rango.Maximo)
            {
                throw new ExcepcionDeDominio($"El rango de {nombre} no es válido: el mínimo debe ser menor que el máximo.");
            }
        }

        Fc = fc;
        Fr = fr;
        Spo2 = spo2;
        Temperatura = temperatura;
        Pas = pas;
        Pad = pad;
    }

    public RangoFisiologico Fc { get; }
    public RangoFisiologico Fr { get; }
    public RangoFisiologico Spo2 { get; }
    public RangoFisiologico Temperatura { get; }
    public RangoFisiologico Pas { get; }
    public RangoFisiologico Pad { get; }

    public RangoFisiologico De(VariableSigno variable) => variable switch
    {
        VariableSigno.Fc => Fc,
        VariableSigno.Fr => Fr,
        VariableSigno.Spo2 => Spo2,
        VariableSigno.Temperatura => Temperatura,
        VariableSigno.Pas => Pas,
        VariableSigno.Pad => Pad,
        VariableSigno.Bateria => Bateria,
        _ => throw new ArgumentOutOfRangeException(nameof(variable)),
    };

    public bool EsValido(VariableSigno variable, decimal valor) => De(variable).Contiene(valor);
}
