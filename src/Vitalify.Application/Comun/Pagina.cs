namespace Vitalify.Application.Comun;

public sealed record Pagina<T>(IReadOnlyList<T> Elementos, int NumeroPagina, int Tamano, int Total)
{
    public int TotalPaginas => Tamano == 0 ? 0 : (int)Math.Ceiling(Total / (double)Tamano);

    public Pagina<TDestino> Mapear<TDestino>(Func<T, TDestino> mapeo) =>
        new(Elementos.Select(mapeo).ToList(), NumeroPagina, Tamano, Total);
}
