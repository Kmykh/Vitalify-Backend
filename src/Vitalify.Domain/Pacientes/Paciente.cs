using System.Text.RegularExpressions;
using Vitalify.Domain.Comun;

namespace Vitalify.Domain.Pacientes;

/// <summary>
/// Persona atendida. Se identifica por su documento y se conserva entre hospitalizaciones: si vuelve a
/// ingresar, se reutiliza el mismo registro. Nunca se borra.
/// </summary>
public sealed partial class Paciente
{
    public const int LargoMinimoNombre = 3;
    public const int LargoMaximoNombre = 150;
    public const int LargoMaximoDocumento = 12;
    public const int EdadMaxima = 120;

    public Guid Id { get; private set; }
    public string NombreCompleto { get; private set; } = null!;
    public TipoDocumento TipoDocumento { get; private set; }
    public string NumeroDocumento { get; private set; } = null!;
    public DateOnly FechaNacimiento { get; private set; }

    /// <summary>La fecha se estimó a partir de la edad (1 de enero del año correspondiente).</summary>
    public bool FechaNacimientoEstimada { get; private set; }

    public DateTime CreadoEn { get; private set; }
    public DateTime ActualizadoEn { get; private set; }

    private Paciente()
    {
    }

    public static Paciente Registrar(
        string nombreCompleto, TipoDocumento tipoDocumento, string numeroDocumento,
        DateOnly fechaNacimiento, bool fechaEstimada, DateTime ahora)
    {
        var numero = NormalizarDocumento(numeroDocumento);
        if (!DocumentoValido(tipoDocumento, numero))
        {
            throw new ExcepcionDeDominio("El documento de identidad no es válido.");
        }

        var paciente = new Paciente
        {
            Id = Guid.NewGuid(),
            TipoDocumento = tipoDocumento,
            NumeroDocumento = numero,
            CreadoEn = ahora,
        };
        paciente.ActualizarDatos(nombreCompleto, fechaNacimiento, fechaEstimada, ahora);
        return paciente;
    }

    public void ActualizarDatos(string nombreCompleto, DateOnly fechaNacimiento, bool fechaEstimada, DateTime ahora)
    {
        var nombre = nombreCompleto?.Trim() ?? string.Empty;
        if (nombre.Length is < LargoMinimoNombre or > LargoMaximoNombre)
        {
            throw new ExcepcionDeDominio($"El nombre debe tener entre {LargoMinimoNombre} y {LargoMaximoNombre} caracteres.");
        }

        if (!FechaNacimientoValida(fechaNacimiento, DateOnly.FromDateTime(ahora)))
        {
            throw new ExcepcionDeDominio($"La fecha de nacimiento no puede ser futura ni implicar más de {EdadMaxima} años.");
        }

        NombreCompleto = nombre;
        FechaNacimiento = fechaNacimiento;
        FechaNacimientoEstimada = fechaEstimada;
        ActualizadoEn = ahora;
    }

    /// <summary>Años cumplidos a la fecha <paramref name="hoy"/>.</summary>
    public int Edad(DateOnly hoy) => CalcularEdad(FechaNacimiento, hoy);

    public static int CalcularEdad(DateOnly fechaNacimiento, DateOnly hoy)
    {
        var edad = hoy.Year - fechaNacimiento.Year;
        return fechaNacimiento.AddYears(edad) > hoy ? edad - 1 : edad;
    }

    /// <summary>Cuando solo se conoce la edad, se estima el nacimiento el 1 de enero del año correspondiente.</summary>
    public static DateOnly FechaEstimadaDesdeEdad(int edad, DateOnly hoy) => new(hoy.Year - edad, 1, 1);

    public static bool FechaNacimientoValida(DateOnly fechaNacimiento, DateOnly hoy) =>
        fechaNacimiento <= hoy && CalcularEdad(fechaNacimiento, hoy) <= EdadMaxima;

    public static string NormalizarDocumento(string? numero) => (numero ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>DNI: 8 dígitos. Carné de extranjería y pasaporte: de 6 a 12 caracteres alfanuméricos.</summary>
    public static bool DocumentoValido(TipoDocumento tipo, string? numero)
    {
        var normalizado = NormalizarDocumento(numero);
        return tipo switch
        {
            TipoDocumento.Dni => Dni().IsMatch(normalizado),
            TipoDocumento.CarneExtranjeria or TipoDocumento.Pasaporte => Alfanumerico().IsMatch(normalizado),
            _ => false,
        };
    }

    [GeneratedRegex("^[0-9]{8}$", RegexOptions.CultureInvariant)]
    private static partial Regex Dni();

    [GeneratedRegex("^[A-Z0-9]{6,12}$", RegexOptions.CultureInvariant)]
    private static partial Regex Alfanumerico();
}
