using Vitalify.Domain.Comun;
using Vitalify.Domain.Dispositivos;

namespace Vitalify.Domain.Hospitalizaciones;

/// <summary>
/// Qué sensor estuvo con qué hospitalización y cuándo. Mientras <see cref="LiberadoEn"/> es nulo, la asignación
/// está vigente. Vincular y liberar cambian también el estado del <see cref="Dispositivo"/>, para que ambos no
/// queden en desacuerdo.
/// </summary>
public sealed class AsignacionDispositivo
{
    public Guid Id { get; private set; }
    public Guid DispositivoId { get; private set; }
    public Guid HospitalizacionId { get; private set; }
    public DateTime AsignadoEn { get; private set; }
    public Guid AsignadoPor { get; private set; }
    public DateTime? LiberadoEn { get; private set; }
    public Guid? LiberadoPor { get; private set; }
    public MotivoLiberacion? MotivoLiberacion { get; private set; }

    private AsignacionDispositivo()
    {
    }

    public bool EstaVigente => LiberadoEn is null;

    public static AsignacionDispositivo Vincular(Dispositivo dispositivo, Hospitalizacion hospitalizacion, DateTime ahora, Guid usuarioId)
    {
        ArgumentNullException.ThrowIfNull(dispositivo);
        ArgumentNullException.ThrowIfNull(hospitalizacion);

        if (!hospitalizacion.EstaActiva)
        {
            throw new ExcepcionDeDominio("Solo se puede vincular un sensor a una hospitalización activa.");
        }

        dispositivo.Asignar();
        return new AsignacionDispositivo
        {
            Id = Guid.NewGuid(),
            DispositivoId = dispositivo.Id,
            HospitalizacionId = hospitalizacion.Id,
            AsignadoEn = ahora,
            AsignadoPor = usuarioId,
        };
    }

    public void Liberar(Dispositivo dispositivo, MotivoLiberacion motivo, DateTime ahora, Guid usuarioId)
    {
        ArgumentNullException.ThrowIfNull(dispositivo);

        if (dispositivo.Id != DispositivoId)
        {
            throw new ExcepcionDeDominio("El dispositivo no corresponde a esta asignación.");
        }

        if (!EstaVigente)
        {
            throw new ExcepcionDeDominio("La asignación ya fue liberada.");
        }

        dispositivo.Liberar();
        LiberadoEn = ahora;
        LiberadoPor = usuarioId;
        MotivoLiberacion = motivo;
    }
}
