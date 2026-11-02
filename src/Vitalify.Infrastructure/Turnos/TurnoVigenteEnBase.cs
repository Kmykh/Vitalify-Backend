using Microsoft.Extensions.Logging;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Turnos;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Infrastructure.Turnos;

/// <summary>
/// Enfermeras de turno según la programación. Si nadie está programado en ese momento (respaldo), son todas las
/// enfermeras activas del servicio y, si el servicio no tiene ninguna, todas las enfermeras activas. El respaldo queda
/// en el log, solo con ids y cantidades.
/// </summary>
internal sealed class TurnoVigenteEnBase(
    IRepositorioProgramacion programacion, IRepositorioUsuarios usuarios, CatalogoTurnos catalogo, ILogger<TurnoVigenteEnBase> logger) : ITurnoVigente
{
    public async Task<EnfermerasDeTurno> EnfermerasDeTurnoAsync(Guid servicioId, DateTime instante, CancellationToken ct = default)
    {
        var turnos = catalogo.TurnosEn(instante);
        var programadas = (await programacion.ListarAsync(servicioId, turnos.Min(t => t.Fecha), turnos.Max(t => t.Fecha), ct))
            .Where(p => turnos.Contains(p.Turno))
            .Select(p => p.UsuarioId)
            .Distinct()
            .ToList();
        if (programadas.Count > 0)
        {
            return new EnfermerasDeTurno(programadas, EsRespaldo: false);
        }

        var delServicio = await usuarios.ListarActivosAsync(Rol.Enfermera, servicioId, ct);
        var respaldo = delServicio.Count > 0 ? delServicio : await usuarios.ListarActivosAsync(Rol.Enfermera, null, ct);
        logger.LogWarning(
            "Turnos: el servicio {Servicio} no tiene programación a las {Instante:O}; se usa el respaldo con {Cantidad} enfermeras activas ({Alcance}).",
            servicioId, instante, respaldo.Count, delServicio.Count > 0 ? "del servicio" : "de todos los servicios");
        return new EnfermerasDeTurno(respaldo.Select(u => u.Id).ToList(), EsRespaldo: true);
    }
}
