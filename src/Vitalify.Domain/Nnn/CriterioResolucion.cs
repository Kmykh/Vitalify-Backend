using Vitalify.Domain.Clinica;

namespace Vitalify.Domain.Nnn;

/// <summary>La última valoración de un resultado NOC del diagnóstico y su meta.</summary>
public sealed record EstadoResultadoNoc(double? UltimaPuntuacion, int Meta);

/// <param name="Evaluaciones">Las evaluaciones guardadas en las últimas 24 h, en cualquier orden.</param>
/// <param name="Tomas">Las tomas manuales de las últimas 24 h, en cualquier orden.</param>
public sealed record DatosResolucion(
    DiagnosticoPlan Diagnostico,
    IReadOnlyList<EvaluacionRiesgo> Evaluaciones,
    IReadOnlyList<ObservacionEnfermeria> Tomas,
    IReadOnlyList<EstadoResultadoNoc> Resultados,
    bool AlertaRiesgoAbierta,
    DateTime Ahora);

/// <summary>
/// Cuándo proponer a la enfermera "listo para resolver" un diagnóstico. Es una propuesta: cerrar lo decide ella (con un
/// toque y el motivo precargado). Criterio de experto (Fable) a validar con la asesora:
/// <list type="bullet">
/// <item>12 h continuas sin el criterio de la regla que lo propuso (el intervalo de NEWS2 0 de RCP 2017); 24 h en la
/// fiebre (R3 y R10), sin antipirético en las 4 h previas a las tomas;</item>
/// <item>al menos 2 tomas manuales completas (PAS y conciencia) en ese lapso, separadas 4 h o más;</item>
/// <item>la última evaluación completa (un puntaje parcial no cuenta);</item>
/// <item>la última valoración NOC de cada resultado en la meta o mejor;</item>
/// <item>sin alerta de riesgo abierta;</item>
/// <item>dolor (R9): 3 o menos en las 2 últimas tomas;</item>
/// <item>nunca para el riesgo de caídas (00303) ni para un diagnóstico elegido a mano (no tiene regla que medir).</item>
/// </list>
/// </summary>
public static class CriterioResolucion
{
    public static readonly TimeSpan Ventana = TimeSpan.FromHours(12);
    public static readonly TimeSpan VentanaFiebre = TimeSpan.FromHours(24);
    public static readonly TimeSpan SeparacionTomas = TimeSpan.FromHours(4);
    public const int TomasCompletasMinimas = 2;
    public const int DolorResuelto = 3;

    /// <returns>El motivo para cerrarlo (precargado), o null si todavía no.</returns>
    public static string? ListoParaResolver(DatosResolucion datos)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var d = datos.Diagnostico;
        if (!d.EstaActivo || d.DiagnosticoCodigo == ProtocoloNnn.NandaRiesgoCaidas || d.ConfirmadoEn is not { } confirmado
            || ReglasNnn.Todas.FirstOrDefault(r => r.Id == d.ReglaOrigen) is not { } regla)
        {
            return null;
        }

        var fiebre = regla.Id is "R3" or "R10";
        var ventana = fiebre ? VentanaFiebre : Ventana;
        var desde = datos.Ahora - ventana;
        if (confirmado > desde || datos.AlertaRiesgoAbierta)
        {
            return null;
        }

        var evaluaciones = datos.Evaluaciones.Where(e => e.EvaluadaEn >= desde && e.EvaluadaEn <= datos.Ahora).OrderBy(e => e.EvaluadaEn).ToList();
        if (evaluaciones.Count == 0 || !evaluaciones[^1].News2Completo)
        {
            return null;
        }

        // La regla no debe cumplirse en ninguna evaluación de la ventana.
        if (evaluaciones.Any(e => regla.Evaluar(ContextoReglasNnn.Desde(new EntradaMotorNnn(e, [], null, [])))?.Diagnostico == d.DiagnosticoCodigo))
        {
            return null;
        }

        var tomas = datos.Tomas.Where(t => t.ObservadaEn >= desde && t.ObservadaEn <= datos.Ahora).OrderBy(t => t.ObservadaEn).ToList();
        var completas = tomas.Where(t => t.Pas is not null && t.Conciencia is not null).ToList();
        if (completas.Count < TomasCompletasMinimas || completas[^1].ObservadaEn - completas[0].ObservadaEn < SeparacionTomas)
        {
            return null;
        }

        if (fiebre && tomas.Any(t => t.Contexto?.AntipireticoUltimas4h == true))
        {
            return null;
        }

        if (regla.Id == "R9")
        {
            var conDolor = tomas.Where(t => t.Contexto?.Dolor is not null).TakeLast(2).ToList();
            if (conDolor.Count < 2 || conDolor.Any(t => t.Contexto!.Dolor > DolorResuelto))
            {
                return null;
            }
        }

        if (datos.Resultados.Count == 0 || datos.Resultados.Any(r => r.UltimaPuntuacion is not { } p || p < r.Meta))
        {
            return null;
        }

        return $"Sin el criterio de {regla.Id} en {ventana.TotalHours:0} h, con {completas.Count} tomas manuales completas y la meta NOC alcanzada.";
    }
}
