using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Vitalify.Infrastructure.Persistence;

/// <summary>
/// Lee una cadena de conexión de la configuración y la normaliza al formato clave=valor de Npgsql.
/// Acepta también el formato URI (<c>postgresql://usuario:contraseña@host:puerto/base</c>) que muestra
/// el botón Connect de Supabase.
/// Si la cadena no fija el tamaño del pool, usa 5: el pooler gratuito de Supabase admite pocas conexiones.
/// Cada contexto se identifica con su propio <c>Application Name</c> (<c>vitalify-transaccional</c> y
/// <c>vitalify-historial</c>): así cada uno tiene su pool y se distingue en <c>pg_stat_activity</c>.
/// </summary>
public static class CadenaDeConexion
{
    public const string Transaccional = "Transaccional";
    public const string Historial = "Historial";

    /// <summary>Conexiones que la API puede abrir en total contra una misma base (los dos pools sumados).</summary>
    public const int MaximoConexionesPorBase = 10;

    private const int PoolMaximoPorDefecto = 5;

    public static string Obtener(IConfiguration configuration, string nombre)
    {
        var valor = configuration.GetConnectionString(nombre);
        if (string.IsNullOrWhiteSpace(valor))
        {
            throw new InvalidOperationException(
                $"Falta la cadena de conexión '{nombre}'. Defínela como ConnectionStrings__{nombre} en el archivo .env de la raíz del repositorio.");
        }

        var builder = new NpgsqlConnectionStringBuilder(Normalizar(valor));
        if (string.IsNullOrWhiteSpace(builder.ApplicationName))
        {
            builder.ApplicationName = $"vitalify-{nombre.ToLowerInvariant()}";
        }

        return builder.ConnectionString;
    }

    /// <summary>
    /// Si las dos cadenas apuntan a la misma base (mismo host, puerto y base de datos), sus <c>Maximum Pool Size</c>
    /// sumados no pueden pasar de <see cref="MaximoConexionesPorBase"/>: el Session pooler del plan gratuito de Supabase
    /// admite pocas conexiones y la API no debe acapararlas. Falla al arrancar si se pasa.
    /// </summary>
    public static void ValidarPresupuesto(string transaccional, string historial)
    {
        var t = new NpgsqlConnectionStringBuilder(transaccional);
        var h = new NpgsqlConnectionStringBuilder(historial);
        var mismaBase = string.Equals(t.Host, h.Host, StringComparison.OrdinalIgnoreCase)
                        && t.Port == h.Port
                        && string.Equals(t.Database, h.Database, StringComparison.Ordinal);
        if (mismaBase && t.MaxPoolSize + h.MaxPoolSize > MaximoConexionesPorBase)
        {
            throw new InvalidOperationException(
                $"Las cadenas Transaccional e Historial apuntan a la misma base y sus Maximum Pool Size suman " +
                $"{t.MaxPoolSize + h.MaxPoolSize}: el máximo es {MaximoConexionesPorBase}. Baja Maximum Pool Size en el .env.");
        }
    }

    public static string Normalizar(string valor)
    {
        valor = valor.Trim();
        if (!valor.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !valor.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var claveValor = new NpgsqlConnectionStringBuilder(valor);
            if (!DefinePoolMaximo(valor))
            {
                claveValor.MaxPoolSize = PoolMaximoPorDefecto;
            }

            return claveValor.ConnectionString;
        }

        var uri = new Uri(valor);
        var credenciales = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Database = uri.AbsolutePath.Trim('/') is { Length: > 0 } db ? Uri.UnescapeDataString(db) : "postgres",
            Username = Uri.UnescapeDataString(credenciales[0]),
            Password = credenciales.Length > 1 ? Uri.UnescapeDataString(credenciales[1]) : null,
            SslMode = SslMode.Require,
            MaxPoolSize = PoolMaximoPorDefecto,
        };

        // Parámetros de la URI (?sslmode=...&...) sobrescriben los valores por defecto.
        foreach (var par in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var partes = par.Split('=', 2);
            var clave = Uri.UnescapeDataString(partes[0]);
            var dato = partes.Length > 1 ? Uri.UnescapeDataString(partes[1]) : string.Empty;
            builder[clave.Equals("sslmode", StringComparison.OrdinalIgnoreCase) ? "SSL Mode" : clave] = dato;
        }

        return builder.ConnectionString;
    }

    private static bool DefinePoolMaximo(string claveValor)
    {
        var crudo = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = claveValor };
        return crudo.Keys.Cast<string>()
            .Select(k => k.Replace(" ", string.Empty, StringComparison.Ordinal))
            .Any(k => k.Equals("MaximumPoolSize", StringComparison.OrdinalIgnoreCase)
                      || k.Equals("MaxPoolSize", StringComparison.OrdinalIgnoreCase));
    }
}
