using System.Globalization;
using FluentValidation;
using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Hospitalizaciones;
using Vitalify.Domain.Pacientes;

namespace Vitalify.Application.Pacientes;

/// <summary>Datos que se capturan al ingresar al paciente y que la enfermera puede corregir después.</summary>
public interface IDatosBasicosPaciente
{
    string NombreCompleto { get; }

    /// <summary>Fecha en formato <c>AAAA-MM-DD</c>. Si no se conoce, se envía <see cref="Edad"/>.</summary>
    string? FechaNacimiento { get; }

    int? Edad { get; }

    string DiagnosticoIngreso { get; }
}

/// <summary>Reglas comunes al ingreso y a la edición. Un solo mensaje por campo.</summary>
public abstract class DatosBasicosPacienteValidador<T> : AbstractValidator<T>
    where T : IDatosBasicosPaciente
{
    protected DatosBasicosPacienteValidador(IReloj reloj)
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(c => c.NombreCompleto)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("El nombre completo es obligatorio.")
            .Must(n => n.Trim().Length is >= Paciente.LargoMinimoNombre and <= Paciente.LargoMaximoNombre)
            .WithMessage($"El nombre completo debe tener entre {Paciente.LargoMinimoNombre} y {Paciente.LargoMaximoNombre} caracteres.");

        RuleFor(c => c.FechaNacimiento)
            .Must((c, f) => f is not null || c.Edad is not null)
            .WithMessage("Indica la fecha de nacimiento o, si no se conoce, la edad.")
            .Must((c, f) => f is null || c.Edad is null)
            .WithMessage("Envía la fecha de nacimiento o la edad, no ambas.")
            .Must(f => f is null || TryParseFecha(f, out _))
            .WithMessage("La fecha de nacimiento debe tener el formato AAAA-MM-DD.")
            .Must(f => f is null || (TryParseFecha(f, out var fecha) && fecha <= Hoy(reloj)))
            .WithMessage("La fecha de nacimiento no puede ser futura.")
            .Must(f => f is null || (TryParseFecha(f, out var fecha) && Paciente.FechaNacimientoValida(fecha, Hoy(reloj))))
            .WithMessage($"La edad resultante debe estar entre 0 y {Paciente.EdadMaxima} años.");

        RuleFor(c => c.Edad)
            .InclusiveBetween(0, Paciente.EdadMaxima).When(c => c.Edad is not null)
            .WithMessage($"La edad debe estar entre 0 y {Paciente.EdadMaxima} años.");

        RuleFor(c => c.DiagnosticoIngreso)
            .Must(d => !string.IsNullOrWhiteSpace(d)).WithMessage("El diagnóstico de ingreso es obligatorio.")
            .Must(d => d.Trim().Length is >= Hospitalizacion.LargoMinimoDiagnostico and <= Hospitalizacion.LargoMaximoDiagnostico)
            .WithMessage($"El diagnóstico de ingreso debe tener entre {Hospitalizacion.LargoMinimoDiagnostico} y {Hospitalizacion.LargoMaximoDiagnostico} caracteres.");
    }

    public static bool TryParseFecha(string? texto, out DateOnly fecha) =>
        DateOnly.TryParseExact(texto?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha);

    /// <summary>Fecha de nacimiento a guardar y si es estimada (cuando solo se conoce la edad). Asume datos validados.</summary>
    public static (DateOnly Fecha, bool Estimada) ResolverFechaNacimiento(IDatosBasicosPaciente datos, DateOnly hoy) =>
        TryParseFecha(datos.FechaNacimiento, out var fecha)
            ? (fecha, false)
            : (Paciente.FechaEstimadaDesdeEdad(datos.Edad!.Value, hoy), true);

    internal static DateOnly Hoy(IReloj reloj) => DateOnly.FromDateTime(reloj.AhoraUtc);
}
