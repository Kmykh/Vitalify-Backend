using Vitalify.Application.Historial;
using Vitalify.Application.Tests.Fakes;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Application.Tests.Historial;

/// <summary>Retención: las crudas de un episodio egresado hace más de 30 días pasan al resumen de 5 min.</summary>
public class RetencionTests
{
    private readonly Escenario _e = new();

    /// <summary>Un episodio con dos lecturas al ingresar y, si se indica, egresado hace <paramref name="egresoHace"/>.</summary>
    private async Task<Guid> EpisodioAsync(string cama, string dni, string dispositivo, TimeSpan ingresoHace, TimeSpan? egresoHace)
    {
        var sensor = _e.AgregarDispositivo(dispositivo);
        var ficha = await _e.HaceAsync(ingresoHace, async () =>
        {
            var f = await _e.IngresarAsync(cama, dni);
            await _e.VincularAsync(f.Id, sensor);
            await _e.RegistrarLectura().EjecutarAsync(_e.Lectura(new SignosMedidos(Fc: 80, Spo2: 97), dispositivo, segundos: 10));
            await _e.RegistrarLectura().EjecutarAsync(_e.Lectura(new SignosMedidos(Fc: 84, Spo2: 96), dispositivo, segundos: 20));
            return f;
        });
        if (egresoHace is { } hace)
        {
            await _e.HaceAsync(hace, async () =>
            {
                Assert.True((await _e.RegistrarEgreso().EjecutarAsync(new(ficha.Id, "AltaMedica", null, _e.EnfermeraId, null))).EsExito);
                return 0;
            });
        }

        return _e.Hospitalizaciones.Hospitalizaciones.Single(h => h.PacienteId == ficha.Id).Id;
    }

    [Fact]
    public async Task Solo_resume_los_episodios_egresados_hace_mas_de_30_dias()
    {
        var activa = await EpisodioAsync("MED-B-01", "10000001", "ESP32-001", TimeSpan.FromDays(40), null);
        var reciente = await EpisodioAsync("MED-B-02", "10000002", "ESP32-002", TimeSpan.FromDays(40), TimeSpan.FromDays(29));
        var antigua = await EpisodioAsync("MED-B-03", "10000003", "ESP32-003", TimeSpan.FromDays(40), TimeSpan.FromDays(31));

        var resultado = await _e.ResumirLecturasAntiguas().EjecutarAsync();

        Assert.Equal(new ResultadoRetencion(1, 2, 1, 0), resultado);
        Assert.DoesNotContain(_e.Historial.Lecturas, l => l.HospitalizacionId == antigua);
        Assert.Equal(2, _e.Historial.Lecturas.Count(l => l.HospitalizacionId == activa));
        Assert.Equal(2, _e.Historial.Lecturas.Count(l => l.HospitalizacionId == reciente));
        Assert.Equal([antigua], _e.Historial.Resumidas.Keys);
    }

    [Fact]
    public async Task Es_idempotente_y_un_episodio_que_falla_no_frena_a_los_demas()
    {
        var falla = await EpisodioAsync("MED-B-01", "10000001", "ESP32-001", TimeSpan.FromDays(60), TimeSpan.FromDays(50));
        var bien = await EpisodioAsync("MED-B-02", "10000002", "ESP32-002", TimeSpan.FromDays(40), TimeSpan.FromDays(35));
        _e.Historial.ResumenFalla.Add(falla);

        var primera = await _e.ResumirLecturasAntiguas().EjecutarAsync();
        _e.Historial.ResumenFalla.Clear();
        var segunda = await _e.ResumirLecturasAntiguas().EjecutarAsync();
        var tercera = await _e.ResumirLecturasAntiguas().EjecutarAsync();

        Assert.Equal((1, 1), (primera.Hospitalizaciones, primera.Fallidas));
        Assert.Contains(bien, _e.Historial.Resumidas.Keys);
        var (operacion, _) = Assert.Single(_e.Bitacora.Fallos);
        Assert.Equal($"Retención del historial de la hospitalización {falla}", operacion);
        Assert.Equal((1, 0), (segunda.Hospitalizaciones, segunda.Fallidas));
        Assert.Equal(new ResultadoRetencion(0, 0, 0, 0), tercera);
    }

    [Fact]
    public async Task El_historial_de_un_episodio_resumido_avisa_desde_cuando_no_hay_lecturas_crudas()
    {
        // Un episodio de 5 días: sin agrupar se puede pedir completo (máximo 7 días).
        await EpisodioAsync("MED-B-01", "10000001", "ESP32-001", TimeSpan.FromDays(36), TimeSpan.FromDays(31));
        var pacienteId = _e.Hospitalizaciones.Hospitalizaciones.Single().PacienteId;
        await _e.ResumirLecturasAntiguas().EjecutarAsync();

        var historial = await _e.ObtenerHistorialSignos().EjecutarAsync(new(pacienteId));

        Assert.True(historial.EsExito);
        Assert.Empty(historial.Valor.Lecturas);
        Assert.NotNull(historial.Valor.ResumidoHasta);
    }
}
