using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Vitalify.Infrastructure.Persistence;

/// <summary>
/// Lee una cadena de conexión de la configuración y la normaliza al formato clave=valor de Npgsql.
/// Acepta también el formato URI (<c>postgresql://usuario:contraseña@host:puerto/base</c>) que muestra
/// el botón Connect de Supabase.
/// Si la cadena no fija el tamaño del pool, usa 5: el pooler gratuito de Supabase admite pocas conexiones.
/// </summary>
public static class CadenaDeConexion
{
    public const string Transaccional = "Transaccional";
    public const string Historial = "Historial";

    private const int PoolMaximoPorDefecto = 5;

    public static string Obtener(IConfiguration configuration, string nombre)
    {
        var valor = configuration.GetConnectionString(nombre);
        if (string.IsNullOrWhiteSpace(valor))
        {
            throw new InvalidOperationException(
                $"Falta la cadena de conexión '{nombre}'. Defínela como ConnectionStrings__{nombre} en el archivo .env de la raíz del repositorio.");
        }

        return Normalizar(valor);
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
