using System.Reflection;

namespace Vitalify.Api.Configuracion;

internal static class InfoServicio
{
    public const string Nombre = "vitalify-api";

    /// <summary>Versión de la API, tomada de <c>&lt;Version&gt;</c> en Directory.Build.props.</summary>
    public static readonly string Version = typeof(InfoServicio).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
        .InformationalVersion.Split('+')[0];
}
