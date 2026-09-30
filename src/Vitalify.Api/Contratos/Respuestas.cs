using System.Text.Json.Serialization;
using Vitalify.Application.Comun;
using Vitalify.Application.Pacientes;

namespace Vitalify.Api.Contratos;

/// <summary>Página de resultados.</summary>
public sealed record PaginaRespuesta<T>(IReadOnlyList<T> Elementos, int Pagina, int Tamano, int Total, int TotalPaginas)
{
    public static PaginaRespuesta<T> Desde(Pagina<T> pagina) =>
        new(pagina.Elementos, pagina.NumeroPagina, pagina.Tamano, pagina.Total, pagina.TotalPaginas);
}

/// <summary>Pacientes en monitoreo continuo (HU07). <c>mensaje</c> solo aparece si la lista está vacía.</summary>
public sealed record PacientesMonitoreadosRespuesta(
    IReadOnlyList<PacienteMonitoreadoDto> Items,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Mensaje)
{
    public static PacientesMonitoreadosRespuesta Desde(PacientesMonitoreadosDto dto) => new(dto.Items, dto.Mensaje);
}
