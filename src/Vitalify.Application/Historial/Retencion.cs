using Vitalify.Application.Puertos;
using Vitalify.Domain.Historial;

namespace Vitalify.Application.Historial;

/// <summary>
/// Sección <c>Historial</c>: cuántos días se guardan crudas las lecturas de un episodio cerrado y a qué hora local corre
/// la retención cada día.
/// </summary>
/// <param name="RetencionHabilitada">Con false el worker no corre (las pruebas llaman al caso de uso directamente).</param>
public sealed record OpcionesHistorial(PoliticaRetencion Retencion, TimeOnly HoraRetencion, TimeZoneInfo Zona, bool RetencionHabilitada = true)
{
    public static OpcionesHistorial PorDefecto { get; } = new(PoliticaRetencion.PorDefecto, new TimeOnly(3, 0), TimeZoneInfo.Utc);
}

/// <param name="Hospitalizaciones">Episodios resumidos en esta vuelta.</param>
/// <param name="Lecturas">Lecturas crudas que quedaron resumidas y se borraron.</param>
/// <param name="Fallidas">Episodios que fallaron (quedan en la bitácora y se reintentan al día siguiente).</param>
public sealed record ResultadoRetencion(int Hospitalizaciones, int Lecturas, int Grupos, int Fallidas);

/// <summary>
/// Retención del historial (lo corre <c>RetencionHistorialWorker</c> una vez al día): las lecturas crudas de cada
/// episodio cuyo egreso cumplió los días de retención pasan a <c>lectura_signos_resumen</c> (5 min) y se borran. Nunca
/// toca una hospitalización activa ni un episodio egresado hace menos de ese tiempo. Si un episodio falla, sigue con
/// los demás. Es idempotente: un episodio ya resumido no tiene lecturas crudas y no vuelve a aparecer.
/// </summary>
public sealed class ResumirLecturasAntiguas(
    IRepositorioLecturas lecturas,
    IRepositorioHospitalizaciones hospitalizaciones,
    OpcionesHistorial opciones,
    IBitacoraFallos bitacora,
    IReloj reloj)
{
    public async Task<ResultadoRetencion> EjecutarAsync(CancellationToken ct = default)
    {
        var ahora = reloj.AhoraUtc;
        var politica = opciones.Retencion;

        // Un episodio egresado antes del corte tiene todas sus lecturas antes del corte: basta buscar entre estas.
        var candidatas = await lecturas.ListarHospitalizacionesConLecturasCrudasAsync(politica.Corte(ahora), ct);
        if (candidatas.Count == 0)
        {
            return new ResultadoRetencion(0, 0, 0, 0);
        }

        var egresos = await hospitalizaciones.ObtenerEgresosAsync(candidatas, ct);
        var (episodios, borradas, grupos, fallidas) = (0, 0, 0, 0);
        foreach (var (hospitalizacionId, _) in egresos.Where(e => politica.DebeResumirse(e.Value, ahora)).OrderBy(e => e.Value))
        {
            try
            {
                var resumidas = await lecturas.ResumirAsync(hospitalizacionId, ahora, ct);
                episodios++;
                borradas += resumidas.Lecturas;
                grupos += resumidas.Grupos;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                fallidas++;
                bitacora.Registrar($"Retención del historial de la hospitalización {hospitalizacionId}", ex);
            }
        }

        return new ResultadoRetencion(episodios, borradas, grupos, fallidas);
    }
}
