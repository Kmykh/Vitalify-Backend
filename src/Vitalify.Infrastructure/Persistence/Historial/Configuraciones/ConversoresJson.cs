using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Vitalify.Infrastructure.Persistence.Historial.Configuraciones;

/// <summary>
/// Valores del dominio guardados como <c>jsonb</c> (checklist y lecturas de presión, contexto de la toma). Se
/// serializan en camelCase, igual que en la API. Son datos de la toma, nunca del paciente.
/// </summary>
internal static class ConversoresJson
{
    private static readonly JsonSerializerOptions Opciones = new(JsonSerializerDefaults.Web);

    /// <summary>EF no pasa los null al conversor: una columna NULL es una propiedad null.</summary>
    public static ValueConverter<T?, string?> Conversor<T>()
        where T : class =>
        new(v => v == null ? null : JsonSerializer.Serialize(v, Opciones), s => s == null ? null : JsonSerializer.Deserialize<T>(s, Opciones));

    /// <summary>Compara por contenido (las listas no tienen igualdad por valor).</summary>
    public static ValueComparer<IReadOnlyList<T>?> ComparadorLista<T>() =>
        new(
            (a, b) => a == null ? b == null : b != null && a.SequenceEqual(b),
            v => v == null ? 0 : v.Aggregate(0, (hash, x) => HashCode.Combine(hash, x)),
            v => v == null ? null : v.ToList());
}
