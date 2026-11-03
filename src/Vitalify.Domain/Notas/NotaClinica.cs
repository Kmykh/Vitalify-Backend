using Vitalify.Domain.Comun;

namespace Vitalify.Domain.Notas;

public enum TipoNota
{
    /// <summary>Nota de evolución: cómo sigue el paciente y qué se decide.</summary>
    Evolucion = 1,

    /// <summary>Nota de enfermería: valoración, cuidados y respuesta del paciente.</summary>
    Enfermeria = 2,

    /// <summary>Opinión de otro servicio o especialidad.</summary>
    Interconsulta = 3,
}

/// <summary>
/// Nota clínica de una hospitalización (HU22). Solo su autor puede corregirla y solo dentro de
/// <see cref="PlazoEdicion"/> desde que la escribió. Cada corrección guarda el texto anterior en una
/// <see cref="NotaClinicaVersion"/>: el registro nunca se borra ni se pierde (NTS N° 139-MINSA/2018/DGAIN).
/// </summary>
public sealed class NotaClinica
{
    public const int LargoMinimoTexto = 5;
    public const int LargoMaximoTexto = 4000;

    public static readonly TimeSpan PlazoEdicion = TimeSpan.FromHours(24);

    public Guid Id { get; private set; }
    public Guid HospitalizacionId { get; private set; }
    public Guid PacienteId { get; private set; }
    public Guid AutorId { get; private set; }
    public TipoNota Tipo { get; private set; }
    public string Texto { get; private set; } = null!;
    public DateTime CreadaEn { get; private set; }

    /// <summary>Momento de la última corrección (null si nunca se editó).</summary>
    public DateTime? EditadaEn { get; private set; }

    /// <summary>Número de la versión vigente: 1 al crearla y uno más con cada corrección.</summary>
    public int Version { get; private set; }

    private NotaClinica()
    {
    }

    public static NotaClinica Registrar(Guid hospitalizacionId, Guid pacienteId, Guid autorId, TipoNota tipo, string texto, DateTime ahora)
    {
        ExigirTipo(tipo);
        return new NotaClinica
        {
            Id = Guid.NewGuid(),
            HospitalizacionId = hospitalizacionId,
            PacienteId = pacienteId,
            AutorId = autorId,
            Tipo = tipo,
            Texto = Limpiar(texto),
            CreadaEn = ahora,
            Version = 1,
        };
    }

    /// <summary>Por qué <paramref name="usuarioId"/> no puede corregirla en <paramref name="ahora"/>, o null si puede.</summary>
    public MotivoNoEditable? PorQueNoEditable(Guid usuarioId, DateTime ahora) =>
        usuarioId != AutorId ? MotivoNoEditable.OtroAutor
        : ahora - CreadaEn > PlazoEdicion ? MotivoNoEditable.FueraDePlazo
        : null;

    /// <summary>
    /// Corrige la nota y devuelve la versión anterior, que hay que guardar. Si el texto y el tipo no cambian, no hay
    /// versión nueva y devuelve null.
    /// </summary>
    public NotaClinicaVersion? Editar(Guid usuarioId, TipoNota tipo, string texto, DateTime ahora)
    {
        switch (PorQueNoEditable(usuarioId, ahora))
        {
            case MotivoNoEditable.OtroAutor:
                throw new ExcepcionDeDominio("Solo el autor puede corregir la nota.");
            case MotivoNoEditable.FueraDePlazo:
                throw new ExcepcionDeDominio($"La nota solo se puede corregir dentro de las {PlazoEdicion.TotalHours:0} h siguientes a su registro.");
        }

        ExigirTipo(tipo);
        var nuevo = Limpiar(texto);
        if (nuevo == Texto && tipo == Tipo)
        {
            return null;
        }

        var anterior = NotaClinicaVersion.De(this, ahora);
        Tipo = tipo;
        Texto = nuevo;
        EditadaEn = ahora;
        Version++;
        return anterior;
    }

    private static string Limpiar(string texto)
    {
        var limpio = texto?.Trim() ?? string.Empty;
        if (limpio.Length is < LargoMinimoTexto or > LargoMaximoTexto)
        {
            throw new ExcepcionDeDominio($"El texto de la nota debe tener entre {LargoMinimoTexto} y {LargoMaximoTexto} caracteres.");
        }

        return limpio;
    }

    private static void ExigirTipo(TipoNota tipo)
    {
        if (!Enum.IsDefined(tipo))
        {
            throw new ExcepcionDeDominio("El tipo de nota no es válido.");
        }
    }
}

public enum MotivoNoEditable
{
    OtroAutor = 1,
    FueraDePlazo = 2,
}

/// <summary>Texto que tuvo una nota antes de una corrección. Nunca se modifica ni se borra.</summary>
public sealed class NotaClinicaVersion
{
    public Guid Id { get; private set; }
    public Guid NotaId { get; private set; }

    /// <summary>Número que tenía esta versión (1 es el texto original).</summary>
    public int Numero { get; private set; }

    public TipoNota Tipo { get; private set; }
    public string Texto { get; private set; } = null!;

    /// <summary>Desde cuándo valía este texto: la creación o la corrección anterior.</summary>
    public DateTime VigenteDesde { get; private set; }

    /// <summary>Cuándo lo reemplazó la corrección siguiente.</summary>
    public DateTime ReemplazadaEn { get; private set; }

    private NotaClinicaVersion()
    {
    }

    internal static NotaClinicaVersion De(NotaClinica nota, DateTime reemplazadaEn) => new()
    {
        Id = Guid.NewGuid(),
        NotaId = nota.Id,
        Numero = nota.Version,
        Tipo = nota.Tipo,
        Texto = nota.Texto,
        VigenteDesde = nota.EditadaEn ?? nota.CreadaEn,
        ReemplazadaEn = reemplazadaEn,
    };
}
