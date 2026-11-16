using System.Globalization;
using System.Text;

namespace Vitalify.Domain.Notas;

/// <summary>Las líneas de cada paso del PAE que arma la aplicación con los datos del turno.</summary>
public sealed record SeccionesNotaPae(
    IReadOnlyList<string> Valoracion,
    IReadOnlyList<string> Diagnostico,
    IReadOnlyList<string> Planificacion,
    IReadOnlyList<string> Ejecucion,
    IReadOnlyList<string> Evaluacion);

/// <summary>
/// Redacta el borrador de la nota de enfermería del turno en los cinco pasos del PAE (D.S. 004-2002-SA, art. 9 a: valoración,
/// diagnóstico, planificación, ejecución y evaluación). Es una plantilla propia y determinista (sin IA), solo con números,
/// códigos y etiquetas cortas: ninguna frase de los libros NANDA-I, NIC ni NOC. El borrador no vale hasta que la enfermera
/// lo revisa y lo firma; entonces se guarda como <see cref="NotaClinica"/> (con versiones y 24 h de corrección).
/// </summary>
public static class RedactorNotaPae
{
    public const string AvisoSensor = "Los datos del wearable vienen de un sensor no certificado como dispositivo médico.";

    public const string AvisoBorrador = "Borrador generado por Vitalify con los datos del turno: revíselo y complételo antes de firmar.";

    private const string Vineta = "- ";

    public static string Redactar(string encabezado, SeccionesNotaPae secciones)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encabezado);
        ArgumentNullException.ThrowIfNull(secciones);
        var pasos = new (string Titulo, IReadOnlyList<string> Lineas)[]
        {
            ("1. Valoración", secciones.Valoracion),
            ("2. Diagnóstico de enfermería", secciones.Diagnostico),
            ("3. Planificación", secciones.Planificacion),
            ("4. Ejecución", secciones.Ejecucion),
            ("5. Evaluación", secciones.Evaluacion),
        };

        var pie = $"{AvisoSensor} {AvisoBorrador}";
        var texto = new StringBuilder(encabezado.Trim()).AppendLine();
        foreach (var (titulo, lineas) in pasos)
        {
            texto.AppendLine().AppendLine(titulo);
            if (lineas.Count == 0)
            {
                texto.Append(Vineta).AppendLine("Sin registros en el turno.");
            }

            foreach (var linea in lineas.Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                texto.Append(Vineta).AppendLine(linea.Trim());
            }
        }

        texto.AppendLine().Append(pie);
        return Recortar(texto.ToString(), pie);
    }

    /// <summary>Con coma decimal, como se escribe en el Perú: "38,4".</summary>
    public static string Numero(decimal valor, int decimales = 1) =>
        valor.ToString(decimales == 0 ? "0" : "0." + new string('0', decimales), CultureInfo.InvariantCulture).Replace('.', ',');

    /// <summary>Hora local de Lima (UTC−5) para la nota: "14:05".</summary>
    public static string Hora(DateTime utc) => utc.AddHours(-5).ToString("HH:mm", CultureInfo.InvariantCulture);

    // La nota tiene como máximo NotaClinica.LargoMaximoTexto: si los datos no caben, se recorta el final de las
    // secciones y se conserva el pie.
    private static string Recortar(string texto, string pie)
    {
        if (texto.Length <= NotaClinica.LargoMaximoTexto)
        {
            return texto;
        }

        const string Omitido = "\n(…se omitieron líneas: el detalle completo está en el plan de cuidados y el historial)\n\n";
        var cuerpo = texto[..(NotaClinica.LargoMaximoTexto - pie.Length - Omitido.Length)];
        return cuerpo[..cuerpo.LastIndexOf('\n')] + Omitido + pie;
    }
}
