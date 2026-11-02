using Vitalify.Application.Clinica;
using Vitalify.Application.Comun;
using Vitalify.Application.Tests.Fakes;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Clinica;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Application.Tests.Clinica;

public class MotorClinicoTests
{
    private readonly Escenario _e = new();

    private async Task<Guid> PacienteConSensorAsync()
    {
        var ficha = await _e.IngresarAsync();
        await _e.VincularAsync(ficha.Id, _e.AgregarDispositivo());
        return ficha.Id;
    }

    /// <summary>Lo que envía el ESP32 de Vitalify: FC, SpO2 y temperatura.</summary>
    private Task RegistrarLecturaDelWearableAsync(int fc = 112, int spo2 = 93, decimal temp = 38.3m, int segundos = 0) =>
        _e.RegistrarLectura().EjecutarAsync(_e.Lectura(new SignosMedidos(Fc: fc, Spo2: spo2, Temperatura: temp), segundos: segundos));

    private Task<Resultado<ObservacionRegistradaDto>> Observar(
        Guid pacienteId, int? fr = 22, int? pas = 105, int? pad = 70, string? conciencia = "Alerta", bool? oxigeno = false,
        decimal? temperatura = null, DateTime? observadaEn = null) =>
        _e.RegistrarObservacion().EjecutarAsync(new(pacienteId, fr, pas, pad, temperatura, conciencia, oxigeno, observadaEn, _e.EnfermeraId, "10.0.0.7"));

    [Fact]
    public async Task Cada_lectura_del_wearable_evalua_el_riesgo_con_un_puntaje_parcial_que_declara_lo_que_falta()
    {
        var pacienteId = await PacienteConSensorAsync();

        await RegistrarLecturaDelWearableAsync();

        var evaluacion = Assert.Single(_e.Historial.Evaluaciones);
        Assert.Equal(OrigenEvaluacion.Lectura, evaluacion.Origen);
        Assert.Equal((112, 93, 38.3m), (evaluacion.Fc, evaluacion.Spo2, evaluacion.Temperatura));
        Assert.Equal((5, NivelRiesgoNews2.Medio, false), (evaluacion.News2Total, evaluacion.News2Nivel, evaluacion.News2Completo));

        var riesgo = (await _e.ObtenerRiesgoPaciente().EjecutarAsync(pacienteId)).Valor.UltimaEvaluacion!;
        Assert.Equal(["fr", "oxigenoSuplementario", "pas", "conciencia"], riesgo.News2.Faltantes);
        Assert.Equal(["pas", "fr", "conciencia"], riesgo.Mews.Faltantes);
        Assert.Equal(new Dictionary<string, int> { ["spo2"] = 2, ["fc"] = 2, ["temperatura"] = 1 }, riesgo.News2.Puntos);

        var monitoreado = (await _e.ListarPacientesMonitoreados().EjecutarAsync()).Valor.Items.Single();
        Assert.Equal((5, evaluacion.MewsTotal, "medio", false), (monitoreado.UltimoNews2, monitoreado.UltimoMews, monitoreado.NivelRiesgo, monitoreado.EvaluacionCompleta));
    }

    [Fact]
    public async Task La_observacion_de_enfermeria_completa_el_puntaje_con_lo_que_el_sensor_no_mide()
    {
        var pacienteId = await PacienteConSensorAsync();
        await RegistrarLecturaDelWearableAsync();

        var resultado = await Observar(pacienteId);

        var riesgo = resultado.Valor.Riesgo;
        Assert.Equal(("Observacion", true, true), (riesgo.Origen, riesgo.News2.Completo, riesgo.Mews.Completo));
        // FC 112 → 2, SpO2 93 → 2, T 38,3 → 1, FR 22 → 2, PAS 105 → 1, aire → 0, alerta → 0
        Assert.Equal((8, "alto"), (riesgo.News2.Total, riesgo.News2.Nivel));
        Assert.Contains("emergencia", riesgo.RespuestaSugerida);
        Assert.Single(_e.Historial.Observaciones);
        Assert.Single(_e.Auditoria.De(AccionAuditoria.ObservacionRegistrada));

        // Las lecturas siguientes del wearable siguen combinándose con la observación vigente.
        await RegistrarLecturaDelWearableAsync(fc: 80, spo2: 97, temp: 37.0m, segundos: 10);
        Assert.True(_e.Historial.Evaluaciones[^1].News2Completo);
    }

    [Fact]
    public async Task Gana_el_valor_mas_reciente_entre_el_sensor_y_la_observacion()
    {
        var pacienteId = await PacienteConSensorAsync();
        await RegistrarLecturaDelWearableAsync(temp: 34.5m);
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(5));

        var conTermometro = await Observar(pacienteId, fr: null, pas: null, pad: null, conciencia: null, oxigeno: null, temperatura: 36.9m);
        Assert.Equal(36.9m, conTermometro.Valor.Riesgo.Parametros.Temperatura);

        await RegistrarLecturaDelWearableAsync(temp: 34.6m, segundos: 5);
        Assert.Equal(34.6m, _e.Historial.Evaluaciones[^1].Temperatura);
    }

    [Fact]
    public async Task El_ajuste_de_temperatura_corrige_la_lectura_periferica_del_sensor()
    {
        _e.Clinicas = OpcionesClinicas.PorDefecto with { AjusteTemperaturaSensor = 2.3m };
        await PacienteConSensorAsync();

        await RegistrarLecturaDelWearableAsync(temp: 34.5m);

        var evaluacion = Assert.Single(_e.Historial.Evaluaciones);
        Assert.Equal(36.8m, evaluacion.Temperatura);
        Assert.Equal(0, evaluacion.News2().Puntos[ParametroClinico.Temperatura]);
    }

    [Fact]
    public async Task Los_valores_del_sensor_que_ya_no_estan_vigentes_y_las_observaciones_vencidas_no_cuentan()
    {
        var pacienteId = await PacienteConSensorAsync();
        await RegistrarLecturaDelWearableAsync();
        await Observar(pacienteId);

        _e.Reloj.Avanzar(TimeSpan.FromHours(4) + TimeSpan.FromMinutes(1));
        var riesgo = await _e.Evaluador().EvaluarAsync(_e.Hospitalizaciones.Hospitalizaciones.Single().Id, pacienteId, OrigenEvaluacion.Lectura);

        Assert.Equal(0, riesgo.News2Total);
        Assert.Equal(7, riesgo.News2().Faltantes.Count);
    }

    [Fact]
    public async Task Una_lectura_atrasada_del_bufer_no_dispara_una_evaluacion()
    {
        await PacienteConSensorAsync();
        await RegistrarLecturaDelWearableAsync();

        await RegistrarLecturaDelWearableAsync(segundos: -120);

        Assert.Equal(2, _e.Historial.Lecturas.Count);
        Assert.Single(_e.Historial.Evaluaciones);
    }

    [Theory]
    [InlineData(null, null, null, null, null, "observacion")]
    [InlineData(18, 120, null, null, null, "pas")]
    [InlineData(18, 80, 90, null, null, "pas")]
    [InlineData(90, null, null, null, null, "fr")]
    [InlineData(null, null, null, "Dormido", null, "conciencia")]
    public async Task La_observacion_se_valida(int? fr, int? pas, int? pad, string? conciencia, bool? oxigeno, string campo)
    {
        var pacienteId = await PacienteConSensorAsync();

        var resultado = await Observar(pacienteId, fr, pas, pad, conciencia, oxigeno);

        Assert.Equal(TipoError.Validacion, resultado.Error.Tipo);
        Assert.Contains(campo, resultado.Error.Detalles.Keys);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(-5 * 60)]
    public async Task Una_observacion_futura_o_de_mas_de_4_horas_se_rechaza(int minutos)
    {
        var pacienteId = await PacienteConSensorAsync();

        var resultado = await Observar(pacienteId, observadaEn: _e.Reloj.AhoraUtc.AddMinutes(minutos));

        Assert.Contains("observadaEn", resultado.Error.Detalles.Keys);
    }

    [Fact]
    public async Task Sin_evaluaciones_el_riesgo_es_nulo_y_sin_hospitalizacion_no_se_encuentra()
    {
        var pacienteId = await PacienteConSensorAsync();

        var sinEvaluacion = await _e.ObtenerRiesgoPaciente().EjecutarAsync(pacienteId);
        await _e.RegistrarEgreso().EjecutarAsync(new(pacienteId, "AltaMedica", null, _e.EnfermeraId, null));
        var sinHospitalizacion = await _e.ObtenerRiesgoPaciente().EjecutarAsync(pacienteId);

        Assert.Null(sinEvaluacion.Valor.UltimaEvaluacion);
        Assert.Equal("sin-hospitalizacion-activa", sinHospitalizacion.Error.Codigo);
    }

    [Fact]
    public async Task La_presencia_del_wearable_aparece_en_monitoreados_y_en_los_signos()
    {
        var pacienteId = await PacienteConSensorAsync();
        var conexion = async () => (await _e.ListarPacientesMonitoreados().EjecutarAsync()).Valor.Items.Single().ConexionSensor;

        Assert.Equal("desconocida", await conexion());
        Assert.True(_e.RegistrarPresencia().Ejecutar("esp32-001", "online"));
        Assert.Equal("en-linea", await conexion());
        Assert.True(_e.RegistrarPresencia().Ejecutar("ESP32-001", "offline"));
        Assert.Equal("fuera-de-linea", (await _e.ObtenerSignosActuales().EjecutarAsync(pacienteId)).Valor.ConexionSensor);
        Assert.False(_e.RegistrarPresencia().Ejecutar("ESP32-001", "dormido"));
    }
}
