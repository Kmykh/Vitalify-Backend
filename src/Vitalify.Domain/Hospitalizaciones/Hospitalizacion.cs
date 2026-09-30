using Vitalify.Domain.Comun;

namespace Vitalify.Domain.Hospitalizaciones;

/// <summary>
/// Episodio de ingreso de un paciente en una cama. Al egresar se marca como <see cref="EstadoHospitalizacion.Finalizada"/>
/// y se conserva: nunca se borra.
/// </summary>
public sealed class Hospitalizacion
{
    public const int LargoMinimoDiagnostico = 3;
    public const int LargoMaximoDiagnostico = 500;
    public const int LargoMaximoObservacion = 500;

    public Guid Id { get; private set; }
    public Guid PacienteId { get; private set; }
    public Guid CamaId { get; private set; }
    public string DiagnosticoIngreso { get; private set; } = null!;
    public DateTime IngresoEn { get; private set; }

    /// <summary>Usuario que registró el ingreso.</summary>
    public Guid RegistradoPor { get; private set; }

    public DateTime? EgresoEn { get; private set; }
    public MotivoEgreso? MotivoEgreso { get; private set; }
    public string? ObservacionEgreso { get; private set; }

    /// <summary>Usuario que registró el egreso.</summary>
    public Guid? EgresoRegistradoPor { get; private set; }

    public EstadoHospitalizacion Estado { get; private set; }

    private Hospitalizacion()
    {
    }

    public bool EstaActiva => Estado == EstadoHospitalizacion.Activa;

    public static Hospitalizacion Abrir(Guid pacienteId, Guid camaId, string diagnosticoIngreso, DateTime ahora, Guid usuarioId)
    {
        var hospitalizacion = new Hospitalizacion
        {
            Id = Guid.NewGuid(),
            PacienteId = pacienteId,
            CamaId = camaId,
            IngresoEn = ahora,
            RegistradoPor = usuarioId,
            Estado = EstadoHospitalizacion.Activa,
        };
        hospitalizacion.ActualizarDiagnostico(diagnosticoIngreso);
        return hospitalizacion;
    }

    public void ActualizarDiagnostico(string diagnosticoIngreso)
    {
        ExigirActiva();
        var diagnostico = diagnosticoIngreso?.Trim() ?? string.Empty;
        if (diagnostico.Length is < LargoMinimoDiagnostico or > LargoMaximoDiagnostico)
        {
            throw new ExcepcionDeDominio(
                $"El diagnóstico de ingreso debe tener entre {LargoMinimoDiagnostico} y {LargoMaximoDiagnostico} caracteres.");
        }

        DiagnosticoIngreso = diagnostico;
    }

    public void RegistrarEgreso(MotivoEgreso motivo, string? observacion, DateTime ahora, Guid usuarioId)
    {
        ExigirActiva();
        if (!Enum.IsDefined(motivo))
        {
            throw new ExcepcionDeDominio("El motivo de egreso no es válido.");
        }

        var observacionLimpia = string.IsNullOrWhiteSpace(observacion) ? null : observacion.Trim();
        if (observacionLimpia?.Length > LargoMaximoObservacion)
        {
            throw new ExcepcionDeDominio($"La observación no puede superar {LargoMaximoObservacion} caracteres.");
        }

        if (ahora < IngresoEn)
        {
            throw new ExcepcionDeDominio("El egreso no puede ser anterior al ingreso.");
        }

        EgresoEn = ahora;
        MotivoEgreso = motivo;
        ObservacionEgreso = observacionLimpia;
        EgresoRegistradoPor = usuarioId;
        Estado = EstadoHospitalizacion.Finalizada;
    }

    private void ExigirActiva()
    {
        if (Estado != EstadoHospitalizacion.Activa)
        {
            throw new ExcepcionDeDominio("La hospitalización ya fue finalizada.");
        }
    }
}
