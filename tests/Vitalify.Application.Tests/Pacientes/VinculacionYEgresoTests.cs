using Vitalify.Application.Comun;
using Vitalify.Application.Pacientes;
using Vitalify.Application.Tests.Fakes;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Dispositivos;
using Vitalify.Domain.Hospitalizaciones;

namespace Vitalify.Application.Tests.Pacientes;

public class VinculacionYEgresoTests
{
    private readonly Escenario _e = new();

    private Task<Resultado<VinculacionDto>> Vincular(Guid pacienteId, Dispositivo dispositivo) =>
        _e.VincularDispositivo().EjecutarAsync(new VincularDispositivoComando(pacienteId, dispositivo.Id, _e.EnfermeraId, "10.0.0.7"));

    [Fact]
    public async Task Vincular_un_sensor_disponible_lo_asigna_y_audita()
    {
        var ficha = await _e.IngresarAsync();
        var sensor = _e.AgregarDispositivo();

        var resultado = await Vincular(ficha.Id, sensor);

        Assert.True(resultado.EsExito);
        Assert.Equal(("ESP32-001", "MED-B-01"), (resultado.Valor.CodigoDispositivo, resultado.Valor.Cama));
        Assert.Equal(EstadoDispositivo.Asignado, sensor.Estado);
        Assert.True(Assert.Single(_e.Hospitalizaciones.Asignaciones).EstaVigente);
        Assert.Single(_e.Auditoria.De(AccionAuditoria.DispositivoVinculado));
    }

    [Fact]
    public async Task Un_sensor_ya_vinculado_devuelve_409_con_la_cama_pero_sin_el_paciente()
    {
        var primero = await _e.IngresarAsync("MED-B-01", "11111111");
        var segundo = await _e.IngresarAsync("MED-B-02", "22222222");
        var sensor = _e.AgregarDispositivo();
        await _e.VincularAsync(primero.Id, sensor);

        var resultado = await Vincular(segundo.Id, sensor);

        Assert.Equal(TipoError.Conflicto, resultado.Error.Tipo);
        Assert.Equal("dispositivo-ya-vinculado", resultado.Error.Codigo);
        Assert.Contains("MED-B-01", resultado.Error.Mensaje);
        Assert.Contains("Libéralo", resultado.Error.Mensaje);
        Assert.DoesNotContain(primero.NombreCompleto, resultado.Error.Mensaje);
    }

    [Theory]
    [InlineData(EstadoDispositivo.Mantenimiento, "dispositivo-en-mantenimiento")]
    [InlineData(EstadoDispositivo.DadoDeBaja, "dispositivo-dado-de-baja")]
    public async Task Un_sensor_en_mantenimiento_o_dado_de_baja_no_se_vincula(EstadoDispositivo estado, string codigo)
    {
        var ficha = await _e.IngresarAsync();
        var sensor = _e.AgregarDispositivo();
        await _e.CambiarEstadoDispositivo().EjecutarAsync(new(sensor.Id, estado.ToString()));

        var resultado = await Vincular(ficha.Id, sensor);

        Assert.Equal(codigo, resultado.Error.Codigo);
        Assert.Empty(_e.Hospitalizaciones.Asignaciones);
    }

    [Fact]
    public async Task Un_paciente_con_sensor_no_recibe_otro_pero_repetir_el_mismo_es_idempotente()
    {
        var ficha = await _e.IngresarAsync();
        var sensor = _e.AgregarDispositivo("ESP32-001");
        var otro = _e.AgregarDispositivo("ESP32-002");
        var vinculacion = await _e.VincularAsync(ficha.Id, sensor);

        var conOtro = await Vincular(ficha.Id, otro);
        var repetido = await Vincular(ficha.Id, sensor);

        Assert.Equal("paciente-ya-tiene-dispositivo", conOtro.Error.Codigo);
        Assert.Equal(vinculacion, repetido.Valor);
        Assert.Single(_e.Hospitalizaciones.Asignaciones);
    }

    [Fact]
    public async Task Sin_hospitalizacion_activa_no_se_vincula()
    {
        var ficha = await _e.IngresarAsync();
        await _e.RegistrarEgreso().EjecutarAsync(new RegistrarEgresoComando(ficha.Id, "AltaMedica", null, _e.EnfermeraId, null));

        var resultado = await Vincular(ficha.Id, _e.AgregarDispositivo());

        Assert.Equal((TipoError.Conflicto, "sin-hospitalizacion-activa"), (resultado.Error.Tipo, resultado.Error.Codigo));
    }

    [Fact]
    public async Task Liberar_devuelve_el_sensor_a_disponible_y_conserva_el_historial()
    {
        var ficha = await _e.IngresarAsync();
        var sensor = _e.AgregarDispositivo();
        await _e.VincularAsync(ficha.Id, sensor);

        var resultado = await _e.LiberarDispositivo().EjecutarAsync(new LiberarDispositivoComando(ficha.Id, _e.EnfermeraId, null));

        Assert.True(resultado.EsExito);
        Assert.Equal(EstadoDispositivo.Disponible, sensor.Estado);
        var asignacion = Assert.Single(_e.Hospitalizaciones.Asignaciones);
        Assert.Equal(MotivoLiberacion.Manual, asignacion.MotivoLiberacion);
        Assert.Null(await _e.ObtenerHospitalizacionActivaPorDispositivo().EjecutarAsync("ESP32-001"));
    }

    [Fact]
    public async Task Liberar_sin_sensor_vinculado_devuelve_no_encontrado()
    {
        var ficha = await _e.IngresarAsync();

        var resultado = await _e.LiberarDispositivo().EjecutarAsync(new LiberarDispositivoComando(ficha.Id, _e.EnfermeraId, null));

        Assert.Equal((TipoError.NoEncontrado, "sin-dispositivo-vinculado"), (resultado.Error.Tipo, resultado.Error.Codigo));
    }

    [Fact]
    public async Task El_egreso_libera_el_sensor_y_cierra_la_hospitalizacion_en_un_solo_guardado()
    {
        var ficha = await _e.IngresarAsync();
        var sensor = _e.AgregarDispositivo();
        await _e.VincularAsync(ficha.Id, sensor);
        var guardadosAntes = _e.UnidadDeTrabajo.Guardados;

        var resultado = await _e.RegistrarEgreso().EjecutarAsync(
            new RegistrarEgresoComando(ficha.Id, "altamedica", "Evolución favorable", _e.EnfermeraId, null));

        Assert.True(resultado.EsExito);
        Assert.Equal(("AltaMedica", "ESP32-001"), (resultado.Valor.Motivo, resultado.Valor.DispositivoLiberado));
        Assert.Equal(guardadosAntes + 1, _e.UnidadDeTrabajo.Guardados);

        var hospitalizacion = Assert.Single(_e.Hospitalizaciones.Hospitalizaciones);
        var asignacion = Assert.Single(_e.Hospitalizaciones.Asignaciones);
        Assert.Equal(EstadoHospitalizacion.Finalizada, hospitalizacion.Estado);
        Assert.Equal((MotivoLiberacion?)MotivoLiberacion.Egreso, asignacion.MotivoLiberacion);
        Assert.Equal(hospitalizacion.EgresoEn, asignacion.LiberadoEn);
        Assert.Equal(EstadoDispositivo.Disponible, sensor.Estado);
        Assert.False(_e.Hospitalizaciones.Ocupada(hospitalizacion.CamaId));
        Assert.Single(_e.Auditoria.De(AccionAuditoria.DispositivoLiberado));
        Assert.Single(_e.Auditoria.De(AccionAuditoria.PacienteEgresado));
    }

    [Fact]
    public async Task El_egreso_sin_sensor_funciona_y_sin_hospitalizacion_activa_es_conflicto()
    {
        var ficha = await _e.IngresarAsync();

        var egreso = await _e.RegistrarEgreso().EjecutarAsync(new RegistrarEgresoComando(ficha.Id, "Traslado", null, _e.EnfermeraId, null));
        var repetido = await _e.RegistrarEgreso().EjecutarAsync(new RegistrarEgresoComando(ficha.Id, "Traslado", null, _e.EnfermeraId, null));

        Assert.Null(egreso.Valor.DispositivoLiberado);
        Assert.Equal((TipoError.Conflicto, "sin-hospitalizacion-activa"), (repetido.Error.Tipo, repetido.Error.Codigo));
    }

    [Fact]
    public async Task El_motivo_de_egreso_debe_ser_valido()
    {
        var ficha = await _e.IngresarAsync();

        var resultado = await _e.RegistrarEgreso().EjecutarAsync(new RegistrarEgresoComando(ficha.Id, "Fuga", null, _e.EnfermeraId, null));

        Assert.Contains("motivo", resultado.Error.Detalles.Keys);
    }

    [Fact]
    public async Task Monitoreados_sin_sensores_devuelve_lista_vacia_con_mensaje()
    {
        await _e.IngresarAsync();

        var resultado = await _e.ListarPacientesMonitoreados().EjecutarAsync();

        Assert.Empty(resultado.Valor.Items);
        Assert.Equal(ListarPacientesMonitoreados.MensajeSinPacientes, resultado.Valor.Mensaje);
    }

    [Fact]
    public async Task Monitoreados_incluye_solo_pacientes_con_sensor_y_sin_datos_de_riesgo()
    {
        var conSensor = await _e.IngresarAsync("MED-B-01", "11111111");
        await _e.IngresarAsync("MED-B-02", "22222222");
        await _e.VincularAsync(conSensor.Id, _e.AgregarDispositivo());

        var resultado = await _e.ListarPacientesMonitoreados().EjecutarAsync();

        var item = Assert.Single(resultado.Valor.Items);
        Assert.Null(resultado.Valor.Mensaje);
        Assert.Equal((conSensor.Id, "MED-B-01", "ESP32-001", 68), (item.PacienteId, item.Cama, item.CodigoDispositivo, item.Edad));
        Assert.Equal((null, null, "sin-datos"), (item.UltimoNews2, item.UltimoMews, item.NivelRiesgo));
    }

    [Fact]
    public async Task La_hospitalizacion_por_codigo_de_dispositivo_es_la_del_sensor_vinculado()
    {
        var ficha = await _e.IngresarAsync();
        var vinculacion = await _e.VincularAsync(ficha.Id, _e.AgregarDispositivo());

        var encontrada = await _e.ObtenerHospitalizacionActivaPorDispositivo().EjecutarAsync(" esp32-001 ");

        Assert.Equal((vinculacion.HospitalizacionId, ficha.Id), (encontrada!.HospitalizacionId, encontrada.PacienteId));
        Assert.Null(await _e.ObtenerHospitalizacionActivaPorDispositivo().EjecutarAsync("ESP32-999"));
        Assert.Null(await _e.ObtenerHospitalizacionActivaPorDispositivo().EjecutarAsync("código inválido"));
    }
}
