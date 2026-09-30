using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Vitalify.Api.IntegrationTests.Infraestructura;
using Vitalify.Application.Pacientes;
using Vitalify.Domain.Auditoria;

namespace Vitalify.Api.IntegrationTests;

/// <summary>HU05 · Registrar nuevo paciente.</summary>
public class HU05RegistroDePacienteTests(FabricaVitalify fabrica) : PruebaApi(fabrica)
{
    [Fact]
    public async Task HU05_E1_EnfermeraCompletaNombreEdadCamaYDiagnostico_PacienteRegistradoYHabilitadoParaSensor()
    {
        var administrador = await ClienteAdministradorAsync();
        var cama = await administrador.CrearCamaAsync();
        var sensor = await administrador.CrearDispositivoAsync();
        var enfermera = await ClienteEnfermeraAsync();
        var dni = ClienteApi.NuevoDni();

        var respuesta = await enfermera.PostAsJsonAsync("/api/v1/pacientes", new
        {
            nombreCompleto = "Rosa Huamán Quispe",
            tipoDocumento = "Dni",
            numeroDocumento = dni,
            edad = 68,
            camaId = cama.Id,
            diagnosticoIngreso = "Neumonía adquirida en la comunidad",
        });

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var ficha = (await respuesta.Content.ReadFromJsonAsync<FichaPacienteDto>())!;
        Assert.Equal(("Rosa Huamán Quispe", 68, true), (ficha.NombreCompleto, ficha.Edad, ficha.FechaNacimientoEstimada));
        Assert.Equal(cama.Codigo, ficha.HospitalizacionActiva!.Cama.Codigo);
        Assert.Equal("Neumonía adquirida en la comunidad", ficha.HospitalizacionActiva.DiagnosticoIngreso);
        Assert.Equal(HttpStatusCode.OK, (await enfermera.GetAsync(respuesta.Headers.Location)).StatusCode);

        // Habilitado para vincularle un sensor.
        Assert.Equal(HttpStatusCode.OK, (await enfermera.VincularAsync(ficha.Id, sensor.Id)).StatusCode);

        // La auditoría registra el ingreso sin nombre ni documento.
        var registros = await Fabrica.ConsultarAsync(db => db.Auditoria
            .Where(a => a.Detalle!.Contains(ficha.Id.ToString())).ToListAsync());
        Assert.Contains(registros, r => r.Accion == AccionAuditoria.PacienteIngresado);
        Assert.All(registros, r => Assert.DoesNotContain("Rosa", r.Detalle));
        Assert.All(registros, r => Assert.DoesNotContain(dni, r.Detalle));
    }

    [Fact]
    public async Task HU05_E2_CamposObligatoriosVacios_Devuelve400ConUnErrorPorCampo()
    {
        var enfermera = await ClienteEnfermeraAsync();

        var respuesta = await enfermera.PostAsJsonAsync("/api/v1/pacientes", new { });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var problema = (await respuesta.Content.ReadFromJsonAsync<ValidationProblemDetails>())!;
        Assert.Equal("validacion", problema.Type);
        Assert.Equal(
            ["camaId", "diagnosticoIngreso", "fechaNacimiento", "nombreCompleto", "numeroDocumento", "tipoDocumento"],
            problema.Errors.Keys.Order());
        Assert.All(problema.Errors.Values, mensajes => Assert.Single(mensajes));
        Assert.Equal("Indica la fecha de nacimiento o, si no se conoce, la edad.", problema.Errors["fechaNacimiento"].Single());
    }

    [Fact]
    public async Task UnaCamaOcupada_Devuelve409()
    {
        var cama = await (await ClienteAdministradorAsync()).CrearCamaAsync();
        var enfermera = await ClienteEnfermeraAsync();
        await enfermera.IngresarPacienteAsync(cama.Id);

        var respuesta = await enfermera.IngresarAsync(cama.Id, ClienteApi.NuevoDni());

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal("cama-ocupada", (await respuesta.LeerProblemaAsync()).Type);
    }

    [Fact]
    public async Task UnPacienteConHospitalizacionActiva_NoPuedeIngresarDeNuevo()
    {
        var administrador = await ClienteAdministradorAsync();
        var (cama1, cama2) = (await administrador.CrearCamaAsync(), await administrador.CrearCamaAsync());
        var enfermera = await ClienteEnfermeraAsync();
        var dni = ClienteApi.NuevoDni();
        await enfermera.IngresarPacienteAsync(cama1.Id, dni);

        var respuesta = await enfermera.IngresarAsync(cama2.Id, dni);

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal("paciente-ya-hospitalizado", (await respuesta.LeerProblemaAsync()).Type);
    }

    [Fact]
    public async Task ElReingresoDespuesDelEgreso_ReutilizaElMismoPaciente()
    {
        var administrador = await ClienteAdministradorAsync();
        var (cama1, cama2) = (await administrador.CrearCamaAsync(), await administrador.CrearCamaAsync());
        var enfermera = await ClienteEnfermeraAsync();
        var dni = ClienteApi.NuevoDni();
        var primera = await enfermera.IngresarPacienteAsync(cama1.Id, dni);
        Assert.Equal(HttpStatusCode.OK, (await enfermera.EgresarAsync(primera.Id)).StatusCode);

        var reingreso = await enfermera.IngresarPacienteAsync(cama2.Id, dni);

        Assert.Equal(primera.Id, reingreso.Id);
        Assert.NotEqual(primera.HospitalizacionActiva!.Id, reingreso.HospitalizacionActiva!.Id);
        Assert.Equal(1, await Fabrica.ConsultarAsync(db => db.Pacientes.CountAsync(p => p.NumeroDocumento == dni)));
        Assert.Equal(2, await Fabrica.ConsultarAsync(db => db.Hospitalizaciones.CountAsync(h => h.PacienteId == primera.Id)));
    }

    [Fact]
    public async Task LaEnfermeraCorrigeLosDatosBasicos()
    {
        var cama = await (await ClienteAdministradorAsync()).CrearCamaAsync();
        var enfermera = await ClienteEnfermeraAsync();
        var ficha = await enfermera.IngresarPacienteAsync(cama.Id);

        var respuesta = await enfermera.PutAsJsonAsync($"/api/v1/pacientes/{ficha.Id}", new
        {
            nombreCompleto = "Rosa María Huamán",
            fechaNacimiento = "1958-03-20",
            diagnosticoIngreso = "Neumonía grave",
        });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var actualizada = (await respuesta.Content.ReadFromJsonAsync<FichaPacienteDto>())!;
        Assert.Equal(("Rosa María Huamán", new DateOnly(1958, 3, 20)), (actualizada.NombreCompleto, actualizada.FechaNacimiento));
        Assert.Equal("Neumonía grave", actualizada.HospitalizacionActiva!.DiagnosticoIngreso);
    }
}
