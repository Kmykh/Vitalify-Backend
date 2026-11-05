using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Clinica;
using Vitalify.Domain.Hospitalizaciones;
using Vitalify.Domain.Notas;

namespace Vitalify.Application.Puertos;

/// <summary>Un episodio del paciente con su cama y el servicio de esa cama.</summary>
public sealed record EpisodioLectura(
    Guid HospitalizacionId, Guid PacienteId, EstadoHospitalizacion Estado, DateTime IngresoEn, DateTime? EgresoEn,
    MotivoEgreso? MotivoEgreso, string? ObservacionEgreso, string DiagnosticoIngreso, EscalaSpo2 EscalaSpo2,
    Guid CamaId, string CodigoCama, Guid ServicioId, string CodigoServicio, string NombreServicio);

/// <summary>Un sensor que estuvo vinculado a una hospitalización, con sus fechas.</summary>
public sealed record SensorEpisodioLectura(
    Guid HospitalizacionId, Guid DispositivoId, string CodigoDispositivo, DateTime AsignadoEn, DateTime? LiberadoEn, MotivoLiberacion? MotivoLiberacion);

/// <summary>Promedio, mínimo y máximo de una variable en un grupo.</summary>
public sealed record EstadisticaSerie(decimal Promedio, decimal Minimo, decimal Maximo);

/// <summary>Lecturas del wearable de un intervalo de tiempo, resumidas por variable (null si no hubo ninguna).</summary>
/// <param name="Resumido">Sale de los resúmenes de 5 min de un episodio cerrado (las crudas ya no existen).</param>
public sealed record GrupoLecturas(
    DateTime Inicio, int Lecturas, EstadisticaSerie? Fc, EstadisticaSerie? Fr, EstadisticaSerie? Spo2, EstadisticaSerie? Temperatura,
    EstadisticaSerie? Pas, EstadisticaSerie? Pad, bool Caida, bool Resumido = false);

/// <summary>Mínimo y máximo de una variable, y cuántas mediciones hubo.</summary>
public sealed record RangoValor(decimal Minimo, decimal Maximo, int Mediciones);

/// <summary>Mínimos y máximos de cada variable en un episodio (null si no se midió).</summary>
public sealed record ExtremosSignos(
    RangoValor? Fc, RangoValor? Fr, RangoValor? Spo2, RangoValor? Temperatura, RangoValor? Pas, RangoValor? Pad);

/// <summary>Evaluaciones de riesgo de un intervalo de tiempo.</summary>
/// <param name="Completas">Cuántas tuvieron NEWS2 y MEWS completos.</param>
public sealed record GrupoEvaluaciones(
    DateTime Inicio, int Evaluaciones, int Completas,
    int News2Minimo, int News2Maximo, int News2Ultimo, NivelRiesgoNews2 News2NivelMaximo,
    int MewsMinimo, int MewsMaximo, int MewsUltimo, NivelRiesgoMews MewsNivelMaximo);

public sealed record FiltroAuditoria(AccionAuditoria? Accion, Guid? UsuarioId, DateTime? Desde, DateTime? Hasta);

/// <summary>Notas clínicas y sus versiones (esquema <c>vitalify</c>). Se guardan con <see cref="IUnidadDeTrabajo"/>.</summary>
public interface IRepositorioNotas
{
    Task<NotaClinica?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Notas de la hospitalización, la más reciente primero; con <paramref name="limite"/>, solo esas.</summary>
    Task<IReadOnlyList<NotaClinica>> ListarDeHospitalizacionAsync(Guid hospitalizacionId, int? limite = null, CancellationToken ct = default);

    /// <summary>Versiones anteriores de la nota, de la más reciente a la original.</summary>
    Task<IReadOnlyList<NotaClinicaVersion>> ListarVersionesAsync(Guid notaId, CancellationToken ct = default);

    void Agregar(NotaClinica nota);

    void AgregarVersion(NotaClinicaVersion version);
}
