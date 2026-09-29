using System.Reflection;
using System.Xml.Linq;
using NetArchTest.Rules;

namespace Vitalify.Architecture.Tests;

/// <summary>
/// Reglas de la arquitectura hexagonal: las dependencias solo apuntan hacia el dominio.
/// </summary>
public class ReglasDeDependenciaTests
{
    private const string DomainNs = "Vitalify.Domain";
    private const string ApplicationNs = "Vitalify.Application";
    private const string InfrastructureNs = "Vitalify.Infrastructure";
    private const string ApiNs = "Vitalify.Api";

    private static readonly Assembly Domain = typeof(Domain.AssemblyReference).Assembly;
    private static readonly Assembly Application = typeof(Application.AssemblyReference).Assembly;
    private static readonly Assembly Infrastructure = typeof(Infrastructure.AssemblyReference).Assembly;

    private static readonly string[] Frameworks =
    [
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "Microsoft.AspNetCore",
        "Microsoft.Extensions",
    ];

    [Fact]
    public void Domain_no_depende_de_otras_capas_ni_de_frameworks()
    {
        var resultado = Types.InAssembly(Domain)
            .ShouldNot()
            .HaveDependencyOnAny([ApplicationNs, InfrastructureNs, ApiNs, .. Frameworks])
            .GetResult();

        Assert.True(resultado.IsSuccessful, Describir(resultado));
    }

    [Fact]
    public void Domain_no_referencia_ensamblados_de_otras_capas_ni_de_frameworks()
    {
        var prohibidas = Domain.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("Vitalify.", StringComparison.Ordinal)
                        || Frameworks.Any(f => n.StartsWith(f, StringComparison.Ordinal)))
            .ToList();

        Assert.Empty(prohibidas);
    }

    [Fact]
    public void Domain_no_tiene_paquetes_nuget_ni_referencias_a_proyectos()
    {
        var csproj = XDocument.Load(RutaCsproj("Vitalify.Domain"));

        Assert.Empty(csproj.Descendants("PackageReference"));
        Assert.Empty(csproj.Descendants("ProjectReference"));
    }

    [Fact]
    public void Application_solo_depende_de_Domain()
    {
        var resultado = Types.InAssembly(Application)
            .ShouldNot()
            .HaveDependencyOnAny([InfrastructureNs, ApiNs, "Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.AspNetCore"])
            .GetResult();

        Assert.True(resultado.IsSuccessful, Describir(resultado));
    }

    [Fact]
    public void Application_solo_referencia_a_Domain_entre_los_proyectos()
    {
        var referencias = Application.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n.StartsWith("Vitalify.", StringComparison.Ordinal))
            .ToList();

        Assert.All(referencias, n => Assert.Equal(DomainNs, n));

        var csproj = XDocument.Load(RutaCsproj("Vitalify.Application"));
        var proyectos = csproj.Descendants("ProjectReference")
            .Select(p => Path.GetFileNameWithoutExtension(p.Attribute("Include")!.Value.Replace('\\', '/')))
            .ToList();

        Assert.Equal([DomainNs], proyectos);
    }

    [Fact]
    public void Infrastructure_no_depende_de_Api()
    {
        var resultado = Types.InAssembly(Infrastructure)
            .ShouldNot()
            .HaveDependencyOn(ApiNs)
            .GetResult();

        Assert.True(resultado.IsSuccessful, Describir(resultado));
        Assert.DoesNotContain(Infrastructure.GetReferencedAssemblies(), a => a.Name == ApiNs);
    }

    private static string Describir(TestResult resultado) =>
        resultado.IsSuccessful
            ? string.Empty
            : "Tipos que violan la regla: " + string.Join(", ", resultado.FailingTypeNames ?? []);

    private static string RutaCsproj(string proyecto)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Vitalify.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "src", proyecto, $"{proyecto}.csproj");
    }
}
