using Vitalify.Application.Comun;

namespace Vitalify.Api.Contratos;

/// <summary>Página de resultados.</summary>
public sealed record PaginaRespuesta<T>(IReadOnlyList<T> Elementos, int Pagina, int Tamano, int Total, int TotalPaginas)
{
    public static PaginaRespuesta<T> Desde(Pagina<T> pagina) =>
        new(pagina.Elementos, pagina.NumeroPagina, pagina.Tamano, pagina.Total, pagina.TotalPaginas);
}

/// <summary>Respuesta temporal de <c>GET /monitoreo/resumen</c>.</summary>
public sealed record ResumenMonitoreoRespuesta(string Mensaje, string? Rol);
