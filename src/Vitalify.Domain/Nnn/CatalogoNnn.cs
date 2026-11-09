using Vitalify.Domain.Comun;

namespace Vitalify.Domain.Nnn;

/// <summary>Las tres taxonomías del proceso de enfermería y los indicadores de NOC.</summary>
public enum TaxonomiaNnn
{
    /// <summary>Diagnósticos de enfermería NANDA-I.</summary>
    Nanda = 1,

    /// <summary>Intervenciones NIC.</summary>
    Nic = 2,

    /// <summary>Resultados NOC.</summary>
    Noc = 3,

    /// <summary>Indicadores de un resultado NOC.</summary>
    Indicador = 4,
}

public enum TipoDiagnosticoNanda
{
    /// <summary>Diagnóstico focalizado en el problema.</summary>
    Real = 1,

    Riesgo = 2,
    PromocionSalud = 3,
}

/// <summary>Signo vital con el que se puede proponer la puntuación de un indicador NOC (a partir de sus puntos NEWS2).</summary>
public enum ParametroVital
{
    Fr = 1,
    Spo2 = 2,
    Temperatura = 3,
    Pas = 4,
    Fc = 5,
    Conciencia = 6,
}

/// <summary>Qué sugiere un vínculo del catálogo para un diagnóstico.</summary>
public enum TipoVinculoNnn
{
    Nic = 1,
    Noc = 2,
}

/// <summary>Formato de los códigos de cada taxonomía.</summary>
public static class CodigosNnn
{
    public const int LargoDiagnostico = 5;
    public const int LargoIntervencion = 4;
    public const int LargoResultado = 4;
    public const int LargoIndicador = 6;

    public static bool EsDiagnostico(string? codigo) => SonDigitos(codigo, LargoDiagnostico);

    public static bool EsIntervencion(string? codigo) => SonDigitos(codigo, LargoIntervencion);

    public static bool EsResultado(string? codigo) => SonDigitos(codigo, LargoResultado);

    public static bool EsIndicador(string? codigo) => SonDigitos(codigo, LargoIndicador);

    public static bool EsValido(TaxonomiaNnn taxonomia, string? codigo) => taxonomia switch
    {
        TaxonomiaNnn.Nanda => EsDiagnostico(codigo),
        TaxonomiaNnn.Nic => EsIntervencion(codigo),
        TaxonomiaNnn.Noc => EsResultado(codigo),
        TaxonomiaNnn.Indicador => EsIndicador(codigo),
        _ => false,
    };

    private static bool SonDigitos(string? codigo, int largo) => codigo is not null && codigo.Length == largo && codigo.All(char.IsAsciiDigit);
}

/// <summary>
/// Una entrada del catálogo NNN: solo el código, una etiqueta corta en español y la edición de la que viene. Nunca la
/// definición, las características definitorias ni las actividades: NANDA-I (Thieme) y NIC/NOC (Elsevier) tienen derechos
/// de autor (ver <c>docs/nnn.md</c>). Las entradas sembradas quedan sin verificar hasta que una enfermera asesora coteja el
/// código con su libro; el administrador las marca como verificadas y la semilla ya no las toca.
/// </summary>
public abstract class EntradaCatalogoNnn
{
    public const int LargoMaximoEtiqueta = 120;
    public const int LargoMaximoEdicion = 30;

    public string Codigo { get; protected set; } = null!;
    public string Etiqueta { get; protected set; } = null!;

    /// <summary>Por ejemplo <c>NANDA-I 2024-2026</c>, <c>NIC 8a ed.</c> o <c>NOC 7a ed.</c>.</summary>
    public string Edicion { get; protected set; } = null!;

    public bool Verificado { get; protected set; }
    public Guid? VerificadoPor { get; protected set; }
    public DateTime? VerificadoEn { get; protected set; }
    public bool Activo { get; protected set; } = true;

    public abstract TaxonomiaNnn Taxonomia { get; }

    /// <summary>La enfermera asesora confirmó el código y la etiqueta contra el libro. Idempotente.</summary>
    public void Verificar(Guid administradorId, DateTime ahora)
    {
        if (Verificado)
        {
            return;
        }

        Verificado = true;
        VerificadoPor = administradorId;
        VerificadoEn = ahora;
    }

    protected void Inicializar(string codigo, string etiqueta, string edicion)
    {
        if (!CodigosNnn.EsValido(Taxonomia, codigo))
        {
            throw new ExcepcionDeDominio($"El código {Taxonomia} no tiene el formato esperado.");
        }

        Codigo = codigo;
        CambiarTextos(etiqueta, edicion);
    }

    /// <summary>La semilla corrige la etiqueta o la edición de una entrada sin verificar. Devuelve si cambió algo.</summary>
    protected bool CambiarTextos(string etiqueta, string edicion)
    {
        var e = Requerido(etiqueta, LargoMaximoEtiqueta, "La etiqueta");
        var d = Requerido(edicion, LargoMaximoEdicion, "La edición");
        if (e == Etiqueta && d == Edicion)
        {
            return false;
        }

        Etiqueta = e;
        Edicion = d;
        return true;
    }

    protected static string Requerido(string? texto, int largoMaximo, string nombre)
    {
        var limpio = texto?.Trim() ?? string.Empty;
        return limpio.Length is > 0 and var largo && largo <= largoMaximo
            ? limpio
            : throw new ExcepcionDeDominio($"{nombre} es obligatoria y tiene como máximo {largoMaximo} caracteres.");
    }

    protected static string? Opcional(string? texto, int largoMaximo, string nombre)
    {
        var limpio = string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
        return limpio is null || limpio.Length <= largoMaximo
            ? limpio
            : throw new ExcepcionDeDominio($"{nombre} tiene como máximo {largoMaximo} caracteres.");
    }
}

/// <summary>Diagnóstico de enfermería NANDA-I (código de 5 dígitos, por ejemplo <c>00032</c>).</summary>
public sealed class DiagnosticoNanda : EntradaCatalogoNnn
{
    public const int LargoMaximoDominio = 80;

    /// <summary>Dominio de la taxonomía, por ejemplo "Actividad/reposo".</summary>
    public string? Dominio { get; private set; }

    /// <summary>Clase dentro del dominio, por ejemplo "Respuestas cardiovasculares/pulmonares".</summary>
    public string? Clase { get; private set; }

    public TipoDiagnosticoNanda Tipo { get; private set; }

    public override TaxonomiaNnn Taxonomia => TaxonomiaNnn.Nanda;

    private DiagnosticoNanda()
    {
    }

    public static DiagnosticoNanda Crear(string codigo, string etiqueta, string? dominio, string? clase, TipoDiagnosticoNanda tipo, string edicion)
    {
        var d = new DiagnosticoNanda();
        d.Inicializar(codigo, etiqueta, edicion);
        d.Actualizar(etiqueta, dominio, clase, tipo, edicion);
        return d;
    }

    /// <summary>Para la semilla: solo cambia una entrada que nadie verificó. Devuelve si cambió algo.</summary>
    public bool Actualizar(string etiqueta, string? dominio, string? clase, TipoDiagnosticoNanda tipo, string edicion)
    {
        if (!Enum.IsDefined(tipo))
        {
            throw new ExcepcionDeDominio("El tipo de diagnóstico no es válido.");
        }

        var nuevoDominio = Opcional(dominio, LargoMaximoDominio, "El dominio");
        var nuevaClase = Opcional(clase, LargoMaximoDominio, "La clase");
        var cambio = CambiarTextos(etiqueta, edicion) | nuevoDominio != Dominio | nuevaClase != Clase | tipo != Tipo;
        (Dominio, Clase, Tipo) = (nuevoDominio, nuevaClase, tipo);
        return cambio;
    }
}

/// <summary>Intervención NIC (código de 4 dígitos, por ejemplo <c>3350</c>).</summary>
public sealed class IntervencionNic : EntradaCatalogoNnn
{
    public const int LargoMaximoCampo = 60;

    /// <summary>Campo de la taxonomía NIC, por ejemplo "Fisiológico: complejo".</summary>
    public string? Campo { get; private set; }

    public override TaxonomiaNnn Taxonomia => TaxonomiaNnn.Nic;

    private IntervencionNic()
    {
    }

    public static IntervencionNic Crear(string codigo, string etiqueta, string? campo, string edicion)
    {
        var i = new IntervencionNic();
        i.Inicializar(codigo, etiqueta, edicion);
        i.Actualizar(etiqueta, campo, edicion);
        return i;
    }

    public bool Actualizar(string etiqueta, string? campo, string edicion)
    {
        var nuevoCampo = Opcional(campo, LargoMaximoCampo, "El campo");
        var cambio = CambiarTextos(etiqueta, edicion) | nuevoCampo != Campo;
        Campo = nuevoCampo;
        return cambio;
    }
}

/// <summary>Resultado NOC (código de 4 dígitos, por ejemplo <c>0415</c>) con los extremos de su escala de 1 a 5.</summary>
public sealed class ResultadoNoc : EntradaCatalogoNnn
{
    public const int LargoMaximoEscala = 150;

    /// <summary>Extremos de la escala Likert, por ejemplo "Desviación grave del rango normal (1) a Sin desviación del rango normal (5)".</summary>
    public string Escala { get; private set; } = null!;

    public override TaxonomiaNnn Taxonomia => TaxonomiaNnn.Noc;

    private ResultadoNoc()
    {
    }

    public static ResultadoNoc Crear(string codigo, string etiqueta, string escala, string edicion)
    {
        var r = new ResultadoNoc();
        r.Inicializar(codigo, etiqueta, edicion);
        r.Actualizar(etiqueta, escala, edicion);
        return r;
    }

    public bool Actualizar(string etiqueta, string escala, string edicion)
    {
        var nuevaEscala = Requerido(escala, LargoMaximoEscala, "La escala");
        var cambio = CambiarTextos(etiqueta, edicion) | nuevaEscala != Escala;
        Escala = nuevaEscala;
        return cambio;
    }
}

/// <summary>
/// Indicador de un resultado NOC (código de 6 dígitos: los 4 del resultado y 2 más, por ejemplo <c>080204</c>). Con
/// <see cref="ParametroVital"/>, el motor propone su puntuación a partir de los puntos NEWS2 de ese signo.
/// </summary>
public sealed class IndicadorNoc : EntradaCatalogoNnn
{
    public string ResultadoCodigo { get; private set; } = null!;
    public ParametroVital? ParametroVital { get; private set; }

    public override TaxonomiaNnn Taxonomia => TaxonomiaNnn.Indicador;

    private IndicadorNoc()
    {
    }

    public static IndicadorNoc Crear(string codigo, string resultadoCodigo, string etiqueta, ParametroVital? parametro, string edicion)
    {
        var i = new IndicadorNoc();
        i.Inicializar(codigo, etiqueta, edicion);
        if (!CodigosNnn.EsResultado(resultadoCodigo) || !codigo.StartsWith(resultadoCodigo, StringComparison.Ordinal))
        {
            throw new ExcepcionDeDominio("El indicador debe empezar con el código de su resultado NOC.");
        }

        i.ResultadoCodigo = resultadoCodigo;
        i.Actualizar(etiqueta, parametro, edicion);
        return i;
    }

    public bool Actualizar(string etiqueta, ParametroVital? parametro, string edicion)
    {
        if (parametro is { } p && !Enum.IsDefined(p))
        {
            throw new ExcepcionDeDominio("El parámetro vital no es válido.");
        }

        var cambio = CambiarTextos(etiqueta, edicion) | parametro != ParametroVital;
        ParametroVital = parametro;
        return cambio;
    }
}

/// <summary>Una intervención NIC o un resultado NOC sugerido para un diagnóstico NANDA-I, en el orden en que se muestran.</summary>
public sealed class VinculoNnn
{
    public string DiagnosticoCodigo { get; private set; } = null!;
    public TipoVinculoNnn Tipo { get; private set; }
    public string Codigo { get; private set; } = null!;
    public int Orden { get; private set; }

    private VinculoNnn()
    {
    }

    public static VinculoNnn Crear(string diagnosticoCodigo, TipoVinculoNnn tipo, string codigo, int orden)
    {
        if (!CodigosNnn.EsDiagnostico(diagnosticoCodigo))
        {
            throw new ExcepcionDeDominio("El diagnóstico del vínculo no tiene el formato NANDA-I.");
        }

        var valido = tipo switch
        {
            TipoVinculoNnn.Nic => CodigosNnn.EsIntervencion(codigo),
            TipoVinculoNnn.Noc => CodigosNnn.EsResultado(codigo),
            _ => false,
        };
        if (!valido || orden < 1)
        {
            throw new ExcepcionDeDominio("El vínculo debe ser una intervención NIC o un resultado NOC con orden positivo.");
        }

        return new VinculoNnn { DiagnosticoCodigo = diagnosticoCodigo, Tipo = tipo, Codigo = codigo, Orden = orden };
    }

    public void CambiarOrden(int orden) => Orden = orden >= 1 ? orden : throw new ExcepcionDeDominio("El orden debe ser positivo.");
}
