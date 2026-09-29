using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Vitalify.Infrastructure.Persistence;

/// <summary>
/// Nombra en snake_case las tablas, columnas, claves, claves foráneas e índices del modelo, salvo los que
/// tengan un nombre explícito. A diferencia de EFCore.NamingConventions, no toca la tabla
/// <c>__EFMigrationsHistory</c>, que ya existe en la base con sus columnas originales.
/// </summary>
internal static class ConvencionSnakeCase
{
    public static void Aplicar(ModelBuilder modelBuilder)
    {
        var entidades = modelBuilder.Model.GetEntityTypes().Where(e => e.GetTableName() is not null).ToList();

        // Primero tablas y columnas, porque los nombres por defecto de claves e índices se calculan a partir de ellas.
        foreach (var entidad in entidades)
        {
            entidad.SetTableName(ASnakeCase(entidad.GetTableName()!));
            foreach (var propiedad in entidad.GetProperties().Where(p => p.FindAnnotation(RelationalAnnotationNames.ColumnName) is null))
            {
                propiedad.SetColumnName(ASnakeCase(propiedad.Name));
            }
        }

        foreach (var entidad in entidades)
        {
            foreach (var clave in entidad.GetKeys())
            {
                clave.SetName(ASnakeCase(clave.GetDefaultName()!));
            }

            foreach (var foranea in entidad.GetForeignKeys())
            {
                foranea.SetConstraintName(ASnakeCase(foranea.GetDefaultName()!));
            }

            foreach (var indice in entidad.GetIndexes().Where(i => i.FindAnnotation(RelationalAnnotationNames.Name) is null))
            {
                indice.SetDatabaseName(ASnakeCase(indice.GetDefaultDatabaseName()!));
            }
        }
    }

    /// <summary><c>HashContrasena</c> → <c>hash_contrasena</c>; <c>PK_usuario</c> → <c>pk_usuario</c>.</summary>
    public static string ASnakeCase(string nombre)
    {
        var resultado = new StringBuilder(nombre.Length + 8);
        for (var i = 0; i < nombre.Length; i++)
        {
            var actual = nombre[i];
            if (!char.IsUpper(actual))
            {
                resultado.Append(actual);
                continue;
            }

            var anterior = i > 0 ? nombre[i - 1] : '_';
            var siguienteEsMinuscula = i + 1 < nombre.Length && char.IsLower(nombre[i + 1]);
            if (anterior != '_' && (char.IsLower(anterior) || char.IsDigit(anterior) || siguienteEsMinuscula))
            {
                resultado.Append('_');
            }

            resultado.Append(char.ToLowerInvariant(actual));
        }

        return resultado.ToString();
    }
}
