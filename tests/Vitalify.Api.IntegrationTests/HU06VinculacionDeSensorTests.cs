using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vitalify.Api.IntegrationTests.Infraestructura;
using Vitalify.Application.Comun;
using Vitalify.Application.Dispositivos;
using Vitalify.Application.Pacientes;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Camas;
using Vitalify.Domain.Dispositivos;
using Vitalify.Domain.Hospitalizaciones;
using Vitalify.Domain.Pacientes;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Api.IntegrationTests;

/// <summary>HU06 · Vincular sensor IoMT a la cama del paciente.</summary>
public class HU06VinculacionDeSensorTests(FabricaVitalify fabrica) : PruebaApi(fabrica)
{
    [Fact]
    public async Task HU06_E1_PacienteRegistradoYSensorDisponible_LaVinculacionQuedaConfirmada()
    {
        var administrador = await ClienteAdministradorAsync();
        var cama = await administrador.CrearCamaAsync();
        var sensor = await administrador.CrearDispositivoAsync();
        var enfermera = await ClienteEnfermeraAsync();
        var ficha = await enfermera.IngresarPacienteAsync(cama.Id);

        var respuesta = await enfermera.VincularAsync(ficha.Id, sensor.Id);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var vinculacion = (await respuesta.Content.ReadFromJsonAsync<VinculacionDto>())!;
        Assert.Equal((ficha.Id, sensor.Codigo, cama.Codigo), (vinculacion.PacienteId, vinculacion.CodigoDispositivo, vinculacion.Cama));

        var asignados = (await administrador.GetFromJsonAsync<List<DispositivoDto>>("/api/v1/dispositivos?estado=Asignado"))!;
        Assert.Equal(cama.Codigo, asignados.Single(d => d.Id == sensor.Id).CodigoCama);

        // Consulta que usará la ingesta de la fase 3.
        var hospitalizacion = await ConsultarPorDispositivoAsync(sensor.Codigo);
        Assert.Equal((vinculacion.HospitalizacionId, ficha.Id), (hospitalizacion!.HospitalizacionId, hospitalizacion.PacienteId));
    }

    [Fact]
    public async Task HU06_E2_SensorAsignadoAOtraCamaActiva_Devuelve409YSugiereLiberarloPrimero()
    {
        var administrador = await ClienteAdministradorAsync();
        var (cama1, cama2) = (await administrador.CrearCamaAsync(), await administrador.CrearCamaAsync());
        var sensor = await administrador.CrearDispositivoAsync();
        var enfermera = await ClienteEnfermeraAsync();
        var primero = await enfermera.IngresarPacienteAsync(cama1.Id);
        var segundo = await enfermera.IngresarPacienteAsync(cama2.Id);
        Assert.Equal(HttpStatusCode.OK, (await enfermera.VincularAsync(primero.Id, sensor.Id)).StatusCode);

        var respuesta = await enfermera.VincularAsync(segundo.Id, sensor.Id);

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        var problema = await respuesta.LeerProblemaAsync();
        Assert.Equal("dispositivo-ya-vinculado", problema.Type);
        Assert.Contains(cama1.Codigo, problema.Detail);
        Assert.Contains("Libéralo primero", problema.Detail);
        Assert.DoesNotContain(primero.NombreCompleto, problema.Detail);
    }

    [Fact]
    public async Task UnDispositivoEnMantenimiento_NoSePuedeVincular()
    {
        var administrador = await ClienteAdministradorAsync();
        var cama = await administrador.CrearCamaAsync();
        var sensor = await administrador.CrearDispositivoAsync();
        var cambio = await administrador.PatchAsJsonAsync($"/api/v1/dispositivos/{sensor.Id}/estado", new { estado = "Mantenimiento" });
        Assert.Equal(HttpStatusCode.OK, cambio.StatusCode);
        var enfermera = await ClienteEnfermeraAsync();
        var ficha = await enfermera.IngresarPacienteAsync(cama.Id);

        var respuesta = await enfermera.VincularAsync(ficha.Id, sensor.Id);

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal("dispositivo-en-mantenimiento", (await respuesta.LeerProblemaAsync()).Type);
    }

    [Fact]
    public async Task UnDispositivoAsignadoNoPuedeIrAMantenimiento()
    {
        var administrador = await ClienteAdministradorAsync();
        var cama = await administrador.CrearCamaAsync();
        var sensor = await administrador.CrearDispositivoAsync();
        var enfermera = await ClienteEnfermeraAsync();
        await enfermera.VincularAsync((await enfermera.IngresarPacienteAsync(cama.Id)).Id, sensor.Id);

        var respuesta = await administrador.PatchAsJsonAsync($"/api/v1/dispositivos/{sensor.Id}/estado", new { estado = "Mantenimiento" });

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal("transicion-invalida", (await respuesta.LeerProblemaAsync()).Type);
    }

    [Fact]
    public async Task DosVinculacionesSimultaneasDelMismoDispositivo_SoloQuedaUnaActivaYLaOtraRecibe409()
    {
        var administrador = await ClienteAdministradorAsync();
        var (cama1, cama2) = (await administrador.CrearCamaAsync(), await administrador.CrearCamaAsync());
        var sensor = await administrador.CrearDispositivoAsync();
        var (enfermeraA, enfermeraB) = (await ClienteEnfermeraAsync(), await ClienteEnfermeraAsync());
        var pacienteA = await enfermeraA.IngresarPacienteAsync(cama1.Id);
        var pacienteB = await enfermeraB.IngresarPacienteAsync(cama2.Id);

        var respuestas = await Task.WhenAll(
            enfermeraA.VincularAsync(pacienteA.Id, sensor.Id),
            enfermeraB.VincularAsync(pacienteB.Id, sensor.Id));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], respuestas.Select(r => r.StatusCode).Order());
        Assert.Equal(1, await Fabrica.ConsultarAsync(db =>
            db.AsignacionesDispositivo.CountAsync(a => a.DispositivoId == sensor.Id && a.LiberadoEn == null)));
    }

    [Fact]
    public async Task LaHospitalizacionPorDispositivoEsLaCorrectaYEsNullDespuesDeLiberarElSensor()
    {
        var administrador = await ClienteAdministradorAsync();
        var cama = await administrador.CrearCamaAsync();
        var sensor = await administrador.CrearDispositivoAsync();
        var enfermera = await ClienteEnfermeraAsync();
        var ficha = await enfermera.IngresarPacienteAsync(cama.Id);
        await enfermera.VincularAsync(ficha.Id, sensor.Id);

        var vinculada = await ConsultarPorDispositivoAsync(sensor.Codigo.ToLowerInvariant());
        Assert.Equal((ficha.HospitalizacionActiva!.Id, ficha.Id, cama.Id, sensor.Id),
            (vinculada!.HospitalizacionId, vinculada.PacienteId, vinculada.CamaId, vinculada.DispositivoId));

        Assert.Equal(HttpStatusCode.NoContent, (await enfermera.DeleteAsync($"/api/v1/pacientes/{ficha.Id}/dispositivo")).StatusCode);

        Assert.Null(await ConsultarPorDispositivoAsync(sensor.Codigo));
    }

    [Fact]
    public async Task CadaIndiceParcialSeTraduceAConflictoConSuCodigo()
    {
        var usuarioId = await Fabrica.ConsultarAsync(db => db.Usuarios.Where(u => u.Rol == Rol.Administrador).Select(u => u.Id).FirstAsync());
        var ahora = DateTime.UtcNow;
        var paciente1 = NuevoPaciente(ahora);
        var paciente2 = NuevoPaciente(ahora);
        var paciente3 = NuevoPaciente(ahora);
        var (cama1, cama2, cama3) = (NuevaCama(), NuevaCama(), NuevaCama());
        var (sensor1, sensor2) = (NuevoSensor(), NuevoSensor());
        var hospitalizacion1 = Hospitalizacion.Abrir(paciente1.Id, cama1.Id, "Diagnóstico", ahora, usuarioId);
        var hospitalizacion2 = Hospitalizacion.Abrir(paciente2.Id, cama2.Id, "Diagnóstico", ahora, usuarioId);
        var asignacion1 = AsignacionDispositivo.Vincular(sensor1, hospitalizacion1, ahora, usuarioId);

        Assert.True((await GuardarAsync(db => db.AddRange(
            paciente1, paciente2, paciente3, cama1, cama2, cama3, sensor1, sensor2, hospitalizacion1, hospitalizacion2, asignacion1))).EsExito);

        // Otra hospitalización activa en la misma cama.
        Assert.Equal("cama-ocupada", (await GuardarAsync(db =>
            db.Add(Hospitalizacion.Abrir(paciente3.Id, cama1.Id, "Diagnóstico", ahora, usuarioId)))).Error.Codigo);

        // Otra hospitalización activa del mismo paciente.
        Assert.Equal("paciente-ya-hospitalizado", (await GuardarAsync(db =>
            db.Add(Hospitalizacion.Abrir(paciente1.Id, cama3.Id, "Diagnóstico", ahora, usuarioId)))).Error.Codigo);

        // Otra asignación vigente del mismo dispositivo (con una copia del sensor que aún figura disponible).
        Assert.Equal("dispositivo-ya-vinculado", (await GuardarAsync(db =>
            db.Add(AsignacionDispositivo.Vincular(CopiaDisponible(sensor1), hospitalizacion2, ahora, usuarioId)))).Error.Codigo);

        // Otra asignación vigente de la misma hospitalización (un wearable por paciente).
        Assert.Equal("paciente-ya-tiene-dispositivo", (await GuardarAsync(db =>
            db.Add(AsignacionDispositivo.Vincular(CopiaDisponible(sensor2), hospitalizacion1, ahora, usuarioId)))).Error.Codigo);
    }

    private static Paciente NuevoPaciente(DateTime ahora) =>
        Paciente.Registrar("Paciente de índice", TipoDocumento.Dni, ClienteApi.NuevoDni(), new DateOnly(1980, 1, 1), false, ahora);

    private static Cama NuevaCama() => Cama.Registrar($"IX-{Guid.NewGuid():N}"[..12], "Pruebas");

    private static Dispositivo NuevoSensor() => Dispositivo.Registrar($"IX-{Guid.NewGuid():N}"[..16], null);

    private async Task<Resultado<Unidad>> GuardarAsync(Action<Infrastructure.Persistence.Transaccional.TransaccionalDbContext> preparar)
    {
        await using var scope = Fabrica.Services.CreateAsyncScope();
        preparar(scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.Transaccional.TransaccionalDbContext>());
        return await scope.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync();
    }

    /// <summary>Instancia disponible con el mismo Id, para saltar la validación del dominio y llegar al índice.</summary>
    private static Dispositivo CopiaDisponible(Dispositivo original)
    {
        var copia = Dispositivo.Registrar(original.Codigo, null);
        typeof(Dispositivo).GetProperty(nameof(Dispositivo.Id))!.GetSetMethod(nonPublic: true)!.Invoke(copia, [original.Id]);
        return copia;
    }

    private async Task<HospitalizacionPorDispositivoDto?> ConsultarPorDispositivoAsync(string codigo)
    {
        await using var scope = Fabrica.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ObtenerHospitalizacionActivaPorDispositivo>().EjecutarAsync(codigo);
    }
}
