using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Vitalify.Infrastructure.Persistence;

/// <summary>
/// Valores del dominio guardados como <c>jsonb</c> (checklist y lecturas de presión, contexto de la toma, detalle y
/// notificaciones de las alertas). Se serializan en camelCase, igual que en la API. Nunca datos personales del paciente.
/// </summary>
internal static class ConversoresJson
{
    private static readonly JsonSerializerOptions Opciones = new(JsonSerializerDefaults.Web);

    /// <summary>EF no pasa los null al conversor: una columna NULL es una propiedad null.</summary>
    public static ValueConverter<T?, string?> Conversor<T>()
        where T : class =>
        new(v => v == null ? null : JsonSerializer.Serialize(v, Opciones), s => s == null ? null : JsonSerializer.Deserialize<T>(s, Opciones));

    /// <summary>Lista no nula guardada como arreglo JSON.</summary>
    public static ValueConverter<IReadOnlyList<T>, string> ConversorLista<T>() =>
        new(v => JsonSerializer.Serialize(v, Opciones), s => JsonSerializer.Deserialize<List<T>>(s, Opciones) ?? new List<T>());

    public static ValueComparer<IReadOnlyDictionary<string, string>?> ComparadorDiccionario() =>
        new(
            (a, b) => MismoContenido(a, b),
            v => v == null ? 0 : v.Aggregate(0, (hash, x) => HashCode.Combine(hash, x.Key, x.Value)),
            v => v == null ? null : v.ToDictionary(x => x.Key, x => x.Value));

    private static bool MismoContenido(IReadOnlyDictionary<string, string>? a, IReadOnlyDictionary<string, string>? b) =>
        a == null ? b == null : b != null && a.Count == b.Count && a.All(x => b.TryGetValue(x.Key, out var v) && v == x.Value);

    /// <summary>Compara por contenido (las listas no tienen igualdad por valor).</summary>
    public static ValueComparer<IReadOnlyList<T>?> ComparadorLista<T>() =>
        new(
            (a, b) => a == null ? b == null : b != null && a.SequenceEqual(b),
            v => v == null ? 0 : v.Aggregate(0, (hash, x) => HashCode.Combine(hash, x)),
            v => v == null ? null : v.ToList());
}
