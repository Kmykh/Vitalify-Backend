using static System.FormattableString;
namespace Vitalify.Domain.Telemetria;

/// <summary>Valores tal como llegaron del sensor. Cualquiera puede faltar: cada sensor falla por separado.</summary>
public sealed record SignosMedidos(
    decimal? Fc = null, decimal? Fr = null, decimal? Spo2 = null, decimal? Temperatura = null,
    decimal? Pas = null, decimal? Pad = null, decimal? Bateria = null);

/// <summary>Valores que pasaron la validación, ya redondeados (enteros, y la temperatura a un decimal).</summary>
public sealed record SignosValidos(int? Fc, int? Fr, int? Spo2, decimal? Temperatura, int? Pas, int? Pad, int? Bateria)
{
    /// <summary>Si queda al menos un signo vital. La batería no cuenta.</summary>
    public bool TieneAlgunSigno => Fc is not null || Fr is not null || Spo2 is not null || Temperatura is not null || Pas is not null;
}

/// <summary>Una variable descartada y por qué.</summary>
public sealed record DescarteSigno(TipoIncidencia Tipo, VariableSigno? Variable, decimal? Valor, string Detalle, bool RequiereNuevaLectura);

public sealed record EvaluacionSignos(SignosValidos Validos, IReadOnlyList<DescarteSigno> Descartes)
{
    public bool PresionIncompleta => Descartes.Any(d => d.Tipo == TipoIncidencia.PresionIncompleta);
}

/// <summary>
/// Reglas de validación de una lectura, puras:
/// <list type="bullet">
/// <item>Descarte por variable: una variable fuera de rango se descarta sola y las demás se conservan.</item>
/// <item>La presión es un par: si falta PAS o PAD, o PAS ≤ PAD, se descartan las dos y se pide una nueva lectura
/// (<see cref="TipoIncidencia.PresionIncompleta"/>). Si una está fuera de rango, también se descarta el par.</item>
/// </list>
/// </summary>
public static class EvaluadorSignos
{
    public static EvaluacionSignos Evaluar(SignosMedidos medidos, RangosFisiologicos rangos)
    {
        var descartes = new List<DescarteSigno>();

        int? Entero(VariableSigno variable, decimal? valor) =>
            Validar(variable, valor, rangos, descartes) is { } v ? (int)Math.Round(v, MidpointRounding.AwayFromZero) : null;

        var fc = Entero(VariableSigno.Fc, medidos.Fc);
        var fr = Entero(VariableSigno.Fr, medidos.Fr);
        var spo2 = Entero(VariableSigno.Spo2, medidos.Spo2);
        var temperatura = Validar(VariableSigno.Temperatura, medidos.Temperatura, rangos, descartes) is { } t
            ? Math.Round(t, 1, MidpointRounding.AwayFromZero)
            : (decimal?)null;
        var bateria = Entero(VariableSigno.Bateria, medidos.Bateria);
        var (pas, pad) = EvaluarPresion(medidos.Pas, medidos.Pad, rangos, descartes);

        return new EvaluacionSignos(new SignosValidos(fc, fr, spo2, temperatura, pas, pad, bateria), descartes);
    }

    private static decimal? Validar(VariableSigno variable, decimal? valor, RangosFisiologicos rangos, List<DescarteSigno> descartes)
    {
        if (valor is not { } v)
        {
            return null;
        }

        if (rangos.EsValido(variable, v))
        {
            return v;
        }

        descartes.Add(new DescarteSigno(TipoIncidencia.FueraDeRango, variable, v,
            Invariant($"{variable} = {v} fuera del rango fisiológico posible ({rangos.De(variable)}). Se descarta como error de sensor."), false));
        return null;
    }

    private static (int? Pas, int? Pad) EvaluarPresion(decimal? pas, decimal? pad, RangosFisiologicos rangos, List<DescarteSigno> descartes)
    {
        if (pas is null && pad is null)
        {
            return (null, null);
        }

        if (pas is null || pad is null)
        {
            var (recibida, valor, falta) = pas is null ? (VariableSigno.Pad, pad, "sistólica") : (VariableSigno.Pas, pas, "diastólica");
            descartes.Add(new DescarteSigno(TipoIncidencia.PresionIncompleta, recibida, valor,
                Invariant($"Presión incompleta: falta la {falta}. Se descarta la presión y se solicita una nueva lectura."), true));
            return (null, null);
        }

        var fueraDeRango = false;
        foreach (var (variable, valor) in new[] { (VariableSigno.Pas, pas.Value), (VariableSigno.Pad, pad.Value) })
        {
            if (!rangos.EsValido(variable, valor))
            {
                fueraDeRango = true;
                descartes.Add(new DescarteSigno(TipoIncidencia.FueraDeRango, variable, valor,
                    Invariant($"{variable} = {valor} fuera del rango fisiológico posible ({rangos.De(variable)}). Se descarta el par de presión."), false));
            }
        }

        if (fueraDeRango)
        {
            return (null, null);
        }

        if (pas <= pad)
        {
            descartes.Add(new DescarteSigno(TipoIncidencia.PresionIncompleta, null, null,
                Invariant($"Presión incoherente: PAS ({pas}) debe ser mayor que PAD ({pad}). Se descarta la presión y se solicita una nueva lectura."), true));
            return (null, null);
        }

        return ((int)Math.Round(pas.Value, MidpointRounding.AwayFromZero), (int)Math.Round(pad.Value, MidpointRounding.AwayFromZero));
    }
}
