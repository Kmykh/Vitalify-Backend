using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Vitalify.Api.IntegrationTests.Infraestructura;
using Vitalify.Application.Camas;
using Vitalify.Application.Dispositivos;
using Vitalify.Application.Pacientes;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Dispositivos;
using Vitalify.Domain.Hospitalizaciones;

namespace Vitalify.Api.IntegrationTests;

/// <summary>HU08 · Dar de alta o baja a un paciente.</summary>
public class HU08EgresoTests(FabricaVitalify fabrica) : PruebaApi(fabrica)
{
    [Fact]
    public async Task HU08_E1_EgresoPorAltaMedica_DesvinculaElSensorYArchivaLaHospitalizacion()
    {
        var (enfermera, ficha, sensor, _) = await PacienteConSensorAsync();

        var respuesta = await enfermera.EgresarAsync(ficha.Id, "AltaMedica");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var egreso = (await respuesta.Content.ReadFromJsonAsync<EgresoDto>())!;
        Assert.Equal(("AltaMedica", sensor.Codigo), (egreso.Motivo, egreso.DispositivoLiberado));

        // Archivada: la hospitalización se conserva como Finalizada.
        var hospitalizacion = await Fabrica.ConsultarAsync(db => db.Hospitalizaciones.SingleAsync(h => h.Id == ficha.HospitalizacionActiva!.Id));
        Assert.Equal(EstadoHospitalizacion.Finalizada, hospitalizacion.Estado);
        Assert.Equal(MotivoEgreso.AltaMedica, hospitalizacion.MotivoEgreso);
        Assert.Equal("Evolución favorable", hospitalizacion.ObservacionEgreso);

        // Sensor desvinculado y paciente conservado, sin hospitalización activa.
        Assert.False(await Fabrica.ConsultarAsync(db => db.AsignacionesDispositivo.AnyAsync(a => a.DispositivoId == sensor.Id && a.LiberadoEn == null)));
        var fichaDespues = (await enfermera.GetFromJsonAsync<FichaPacienteDto>($"/api/v1/pacientes/{ficha.Id}"))!;
        Assert.Null(fichaDespues.HospitalizacionActiva);
    }

    [Fact]
    public async Task HU08_E2_EgresoConSensorVinculado_LoLiberaEnLaMismaTransaccion()
    {
        var (enfermera, ficha, sensor, _) = await PacienteConSensorAsync();

        Assert.Equal(HttpStatusCode.OK, (await enfermera.EgresarAsync(ficha.Id, "Traslado")).StatusCode);

        var hospitalizacion = await Fabrica.ConsultarAsync(db => db.Hospitalizaciones.SingleAsync(h => h.Id == ficha.HospitalizacionActiva!.Id));
        var asignacion = await Fabrica.ConsultarAsync(db => db.AsignacionesDispositivo.SingleAsync(a => a.HospitalizacionId == hospitalizacion.Id));
        Assert.Equal(MotivoLiberacion.Egreso, asignacion.MotivoLiberacion);
        Assert.Equal(hospitalizacion.EgresoEn, asignacion.LiberadoEn);
        Assert.Equal(hospitalizacion.EgresoRegistradoPor, asignacion.LiberadoPor);
        Assert.Equal(EstadoDispositivo.Disponible, await Fabrica.ConsultarAsync(db => db.Dispositivos.Where(d => d.Id == sensor.Id).Select(d => d.Estado).SingleAsync()));

        var registros = await Fabrica.ConsultarAsync(db => db.Auditoria
            .Where(a => a.Detalle!.Contains(hospitalizacion.Id.ToString())
                        && (a.Accion == AccionAuditoria.DispositivoLiberado || a.Accion == AccionAuditoria.PacienteEgresado))
            .ToListAsync());
        Assert.Equal(2, registros.Count);
        Assert.Single(registros.Select(r => r.Fecha).Distinct());
    }

    [Fact]
    public async Task DespuesDelEgreso_ElDispositivoVuelveADisponibleYLaCamaQuedaLibre()
    {
        var (enfermera, ficha, sensor, cama) = await PacienteConSensorAsync();

        await enfermera.EgresarAsync(ficha.Id);

        var disponibles = (await enfermera.GetFromJsonAsync<List<DispositivoDto>>("/api/v1/dispositivos?estado=Disponible"))!;
        var camasLibres = (await enfermera.GetFromJsonAsync<List<CamaDto>>("/api/v1/camas?soloDisponibles=true"))!;
        Assert.Contains(disponibles, d => d.Id == sensor.Id);
        Assert.Contains(camasLibres, c => c.Id == cama.Id);
    }

    [Fact]
    public async Task UnEgresoSinHospitalizacionActiva_Devuelve409()
    {
        var (enfermera, ficha, _, _) = await PacienteConSensorAsync();
        await enfermera.EgresarAsync(ficha.Id);

        var respuesta = await enfermera.EgresarAsync(ficha.Id);

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal("sin-hospitalizacion-activa", (await respuesta.LeerProblemaAsync()).Type);
    }
}
