using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vitalify.Domain.Nnn;
using Vitalify.Infrastructure.Persistence.Transaccional;

namespace Vitalify.Infrastructure.Semillas;

/// <summary>Cuántas entradas del catálogo NNN agregó, corrigió o dejó como estaban (verificadas) una pasada de la semilla.</summary>
public sealed record ResultadoSemillaNnn(string Version, int Agregadas, int Actualizadas, int VerificadasSinTocar, int VinculosAgregados);

/// <summary>
/// Carga el catálogo NNN tentativo (<c>Semillas/nnn-catalogo.2026.11.1.json</c>, recurso embebido) al arrancar, en todos
/// los entornos. Es idempotente: agrega lo que falta y corrige la etiqueta o la edición de las entradas sin verificar, pero
/// <b>nunca toca una entrada que el administrador ya verificó</b> y nunca borra. El log lleva solo cantidades, nunca códigos.
/// </summary>
public sealed class SemillaCatalogoNnn(IServiceScopeFactory scopes, ILogger<SemillaCatalogoNnn> logger) : IHostedService
{
    public const string Recurso = "Vitalify.Infrastructure.Semillas.nnn-catalogo.json";

    private static readonly JsonSerializerOptions OpcionesJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var r = await SembrarAsync(cancellationToken);
            logger.LogInformation(
                "Catálogo NNN {Version}: {Agregadas} entradas agregadas, {Actualizadas} corregidas, {Verificadas} verificadas sin tocar y {Vinculos} vínculos agregados.",
                r.Version, r.Agregadas, r.Actualizadas, r.VerificadasSinTocar, r.VinculosAgregados);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "No se pudo cargar el catálogo NNN.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static ArchivoCatalogoNnn LeerArchivo()
    {
        using var flujo = typeof(SemillaCatalogoNnn).Assembly.GetManifestResourceStream(Recurso)
            ?? throw new InvalidOperationException($"No se encontró el recurso embebido {Recurso}.");
        return JsonSerializer.Deserialize<ArchivoCatalogoNnn>(flujo, OpcionesJson)
            ?? throw new InvalidOperationException("El catálogo NNN está vacío.");
    }

    public async Task<ResultadoSemillaNnn> SembrarAsync(CancellationToken ct = default)
    {
        var archivo = LeerArchivo();
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TransaccionalDbContext>();
        var cuenta = new Contador();

        var diagnosticos = await db.DiagnosticosNanda.ToDictionaryAsync(d => d.Codigo, ct);
        foreach (var d in archivo.Diagnosticos)
        {
            Aplicar(diagnosticos.GetValueOrDefault(d.Codigo), cuenta,
                () => db.DiagnosticosNanda.Add(DiagnosticoNanda.Crear(d.Codigo, d.Etiqueta, d.Dominio, d.Clase, d.Tipo, archivo.Ediciones.Nanda)),
                e => ((DiagnosticoNanda)e).Actualizar(d.Etiqueta, d.Dominio, d.Clase, d.Tipo, archivo.Ediciones.Nanda));
        }

        var intervenciones = await db.IntervencionesNic.ToDictionaryAsync(i => i.Codigo, ct);
        foreach (var i in archivo.Intervenciones)
        {
            Aplicar(intervenciones.GetValueOrDefault(i.Codigo), cuenta,
                () => db.IntervencionesNic.Add(IntervencionNic.Crear(i.Codigo, i.Etiqueta, i.Campo, archivo.Ediciones.Nic)),
                e => ((IntervencionNic)e).Actualizar(i.Etiqueta, i.Campo, archivo.Ediciones.Nic));
        }

        var resultados = await db.ResultadosNoc.ToDictionaryAsync(r => r.Codigo, ct);
        foreach (var r in archivo.Resultados)
        {
            Aplicar(resultados.GetValueOrDefault(r.Codigo), cuenta,
                () => db.ResultadosNoc.Add(ResultadoNoc.Crear(r.Codigo, r.Etiqueta, r.Escala, archivo.Ediciones.Noc)),
                e => ((ResultadoNoc)e).Actualizar(r.Etiqueta, r.Escala, archivo.Ediciones.Noc));
        }

        // Los resultados nuevos se guardan antes que sus indicadores (clave foránea).
        await db.SaveChangesAsync(ct);

        var indicadores = await db.IndicadoresNoc.ToDictionaryAsync(i => i.Codigo, ct);
        foreach (var i in archivo.Indicadores)
        {
            Aplicar(indicadores.GetValueOrDefault(i.Codigo), cuenta,
                () => db.IndicadoresNoc.Add(IndicadorNoc.Crear(i.Codigo, i.Resultado, i.Etiqueta, i.ParametroVital, archivo.Ediciones.Noc)),
                e => ((IndicadorNoc)e).Actualizar(i.Etiqueta, i.ParametroVital, archivo.Ediciones.Noc));
        }

        var vinculos = (await db.VinculosNnn.ToListAsync(ct)).ToDictionary(v => (v.DiagnosticoCodigo, v.Tipo, v.Codigo));
        var vinculosAgregados = 0;
        foreach (var v in archivo.Vinculos)
        {
            foreach (var (tipo, codigos) in new[] { (TipoVinculoNnn.Nic, v.Nic), (TipoVinculoNnn.Noc, v.Noc) })
            {
                for (var orden = 1; orden <= codigos.Count; orden++)
                {
                    if (vinculos.TryGetValue((v.Diagnostico, tipo, codigos[orden - 1]), out var existente))
                    {
                        if (existente.Orden != orden)
                        {
                            existente.CambiarOrden(orden);
                        }
                    }
                    else
                    {
                        db.VinculosNnn.Add(VinculoNnn.Crear(v.Diagnostico, tipo, codigos[orden - 1], orden));
                        vinculosAgregados++;
                    }
                }
            }
        }

        await db.SaveChangesAsync(ct);
        return new ResultadoSemillaNnn(archivo.Version, cuenta.Agregadas, cuenta.Actualizadas, cuenta.Verificadas, vinculosAgregados);
    }

    private static void Aplicar(EntradaCatalogoNnn? existente, Contador cuenta, Action agregar, Func<EntradaCatalogoNnn, bool> actualizar)
    {
        if (existente is null)
        {
            agregar();
            cuenta.Agregadas++;
        }
        else if (existente.Verificado)
        {
            cuenta.Verificadas++; // lo verificó una enfermera asesora: la semilla no lo pisa
        }
        else if (actualizar(existente))
        {
            cuenta.Actualizadas++;
        }
    }

    private sealed class Contador
    {
        public int Agregadas { get; set; }
        public int Actualizadas { get; set; }
        public int Verificadas { get; set; }
    }
}

public sealed record EdicionesNnn(string Nanda, string Nic, string Noc);

public sealed record DiagnosticoSemilla(string Codigo, string Etiqueta, string? Dominio, string? Clase, TipoDiagnosticoNanda Tipo);

public sealed record IntervencionSemilla(string Codigo, string Etiqueta, string? Campo);

public sealed record ResultadoSemilla(string Codigo, string Etiqueta, string Escala);

public sealed record IndicadorSemilla(string Codigo, string Resultado, string Etiqueta, ParametroVital? ParametroVital);

public sealed record VinculoSemilla(string Diagnostico, IReadOnlyList<string> Nic, IReadOnlyList<string> Noc);

/// <summary>Formato del archivo de semilla del catálogo NNN.</summary>
public sealed record ArchivoCatalogoNnn(
    string Version, EdicionesNnn Ediciones, IReadOnlyList<DiagnosticoSemilla> Diagnosticos, IReadOnlyList<IntervencionSemilla> Intervenciones,
    IReadOnlyList<ResultadoSemilla> Resultados, IReadOnlyList<IndicadorSemilla> Indicadores, IReadOnlyList<VinculoSemilla> Vinculos);
