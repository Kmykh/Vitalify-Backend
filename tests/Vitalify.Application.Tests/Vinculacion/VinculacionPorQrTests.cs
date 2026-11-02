using System.Text;
using Vitalify.Application.Clinica;
using Vitalify.Application.Comun;
using Vitalify.Application.Tests.Fakes;
using Vitalify.Application.Vinculacion;
using Vitalify.Domain.Clinica;
using Vitalify.Domain.Dispositivos;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Application.Tests.Vinculacion;

/// <summary>JSON exacto de los comandos al wearable: es el contrato con el firmware.</summary>
public class ContratoComandosTests
{
    public static TheoryData<ComandoDispositivo, string, bool> Comandos => new()
    {
        { new MostrarQr("VTF1:ESP32-001:K7P3QX9M", new DateTime(2026, 11, 2, 15, 10, 0, DateTimeKind.Utc)), """{"accion":"mostrar-qr","qr":"VTF1:ESP32-001:K7P3QX9M","expira":"2026-11-02T15:10:00Z"}""", true },
        { new Emparejando(), """{"accion":"emparejando"}""", false },
        { new Vinculado("MED-B-01", "R.M."), """{"accion":"vinculado","cama":"MED-B-01","iniciales":"R.M."}""", true },
        { new Desvinculado(), """{"accion":"desvinculado"}""", true },
        { new RepetirMedicion("presion", "Falta la diastólica"), """{"accion":"repetir-medicion","variable":"presion","motivo":"Falta la diastólica"}""", false },
    };

    [Theory]
    [MemberData(nameof(Comandos))]
    public void Cada_comando_tiene_su_json_y_si_va_retenido(ComandoDispositivo comando, string json, bool retenido)
    {
        Assert.Equal(json, ContratoComandos.SerializarTexto(comando));
        Assert.Equal(retenido, comando.Retenido);
        Assert.Equal("device/ESP32-001/comandos", ContratoComandos.Topico("ESP32-001"));
    }

    [Theory]
    [InlineData("""{"accion":"solicitar-qr"}""", true)]
    [InlineData("""{"accion":"SOLICITAR-QR","extra":1}""", true)]
    [InlineData("""{"accion":"reiniciar"}""", false)]
    [InlineData("solicitar-qr", false)]
    [InlineData("[]", false)]
    public void Solo_se_atiende_la_solicitud_de_qr_del_contrato(string payload, bool esSolicitud) =>
        Assert.Equal(esSolicitud, ContratoComandos.EsSolicitudQr(Encoding.UTF8.GetBytes(payload)));
}

public class VinculacionPorQrTests
{
    private readonly Escenario _e = new();

    private string UltimoQr(string dispositivo) => _e.Comandos.De<MostrarQr>(dispositivo).Last().Qr;

    private async Task<Dispositivo> SensorEnLineaAsync(string codigo = "ESP32-001")
    {
        var sensor = _e.AgregarDispositivo(codigo);
        Assert.True(await _e.RegistrarPresencia().EjecutarAsync(codigo, "online"));
        return sensor;
    }

    private static TomaManualComando TomaInicial() => new()
    {
        Oxigeno = new OxigenoTomado(false),
        Conciencia = new ConcienciaTomada(DespiertoCoherente: true, ConfusionNueva: false),
        Presion = new PresionTomada([new LecturaPresionTomada(120, 80)]),
    };

    private ConfirmarVinculacionComando Confirmacion(string token, Guid camaId, string documento = "45879632") =>
        new(token, new PacienteVinculacion(null, "Rosa Mendoza Quispe", "Dni", documento, "1958-04-12", null, false),
            camaId, "Neumonía adquirida en la comunidad", TomaInicial(), _e.EnfermeraId, "10.0.0.7");

    [Fact]
    public async Task Al_conectarse_un_sensor_disponible_recibe_un_qr_y_uno_en_mantenimiento_nada()
    {
        await SensorEnLineaAsync();
        var mantenimiento = _e.AgregarDispositivo("ESP32-002");
        mantenimiento.EnviarAMantenimiento();

        await _e.RegistrarPresencia().EjecutarAsync("ESP32-002", "online");
        await _e.RegistrarPresencia().EjecutarAsync("DESCONOCIDO", "online");

        var (dispositivo, comando, retenido) = Assert.Single(_e.Comandos.Publicados);
        Assert.Equal(("ESP32-001", "mostrar-qr", true), (dispositivo, comando.Accion, retenido));
    }

    [Fact]
    public async Task Si_se_reconecta_durante_un_escaneo_recibe_emparejando_y_no_invalida_el_token()
    {
        await SensorEnLineaAsync();
        var escaneo = await _e.EscanearQr().EjecutarAsync(new EscanearQrComando(UltimoQr("ESP32-001"), _e.EnfermeraId, null));

        await _e.RegistrarPresencia().EjecutarAsync("ESP32-001", "online");

        Assert.Equal(["mostrar-qr", "emparejando", "emparejando"], _e.Comandos.Publicados.Select(p => p.Comando.Accion));
        var confirmada = await _e.ConfirmarVinculacion().EjecutarAsync(Confirmacion(escaneo.Valor.TokenVinculacion, _e.AgregarCama().Id));
        Assert.True(confirmada.EsExito, confirmada.EsExito ? null : confirmada.Error.Codigo);
    }

    [Fact]
    public async Task La_solicitud_de_qr_genera_uno_nuevo_y_si_esta_vinculado_reenvia_vinculado()
    {
        await SensorEnLineaAsync();
        var solicitud = Encoding.UTF8.GetBytes("""{"accion":"solicitar-qr"}""");

        Assert.Equal(SincronizarPantallaDispositivo.QrPublicado, await _e.AtenderSolicitud().EjecutarAsync("ESP32-001", solicitud));
        Assert.Equal(2, _e.Comandos.De<MostrarQr>("ESP32-001").Count());
        Assert.Null(await _e.AtenderSolicitud().EjecutarAsync("ESP32-001", Encoding.UTF8.GetBytes("""{"accion":"otra"}""")));

        var escaneo = await _e.EscanearQr().EjecutarAsync(new EscanearQrComando(UltimoQr("ESP32-001"), _e.EnfermeraId, null));
        await _e.ConfirmarVinculacion().EjecutarAsync(Confirmacion(escaneo.Valor.TokenVinculacion, _e.AgregarCama().Id));

        Assert.Equal(SincronizarPantallaDispositivo.Vinculado, await _e.AtenderSolicitud().EjecutarAsync("ESP32-001", solicitud));
        Assert.Equal(new Vinculado("MED-B-01", "R.M."), _e.Comandos.Publicados.Last().Comando);
    }

    [Fact]
    public async Task Si_el_historial_falla_la_vinculacion_queda_hecha_y_se_avisa_que_registren_la_toma_de_nuevo()
    {
        var sensor = await SensorEnLineaAsync();
        var escaneo = await _e.EscanearQr().EjecutarAsync(new EscanearQrComando(UltimoQr("ESP32-001"), _e.EnfermeraId, null));
        _e.Historial.ErroresAlGuardar.Enqueue(ErroresComunes.Concurrencia);

        var resultado = await _e.ConfirmarVinculacion().EjecutarAsync(Confirmacion(escaneo.Valor.TokenVinculacion, _e.AgregarCama().Id));

        Assert.True(resultado.EsExito);
        Assert.Equal([AvisosClinicos.ObservacionInicialNoGuardada], resultado.Valor.Respuesta.Avisos);
        Assert.Null(resultado.Valor.Respuesta.Riesgo);
        Assert.NotNull(resultado.Valor.FalloObservacionInicial);
        Assert.Equal(EstadoDispositivo.Asignado, sensor.Estado);
        Assert.Empty(_e.Historial.Observaciones);
        Assert.Equal("vinculado", _e.Comandos.Publicados.Last().Comando.Accion);
        Assert.Contains("ESP32-001", _e.Resolutor.Invalidaciones);
    }

    [Fact]
    public async Task Una_cama_ocupada_no_deja_nada_a_medias()
    {
        var sensor = await SensorEnLineaAsync();
        var ocupada = await _e.IngresarAsync("MED-B-09", "11111111");
        var escaneo = await _e.EscanearQr().EjecutarAsync(new EscanearQrComando(UltimoQr("ESP32-001"), _e.EnfermeraId, null));
        var guardados = _e.UnidadDeTrabajo.Guardados;

        var resultado = await _e.ConfirmarVinculacion().EjecutarAsync(
            Confirmacion(escaneo.Valor.TokenVinculacion, _e.Camas.Camas.Single(c => c.Codigo == "MED-B-09").Id));

        Assert.Equal("cama-ocupada", resultado.Error.Codigo);
        Assert.Equal(guardados, _e.UnidadDeTrabajo.Guardados);
        Assert.Equal(EstadoDispositivo.Disponible, sensor.Estado);
        Assert.NotNull(ocupada);
    }

    [Fact]
    public async Task El_worker_renueva_solo_los_qr_vencidos_de_sensores_en_linea_y_disponibles()
    {
        await SensorEnLineaAsync("ESP32-001");
        await SensorEnLineaAsync("ESP32-002");
        _e.AgregarDispositivo("ESP32-003"); // nunca se conectó
        await _e.RegistrarPresencia().EjecutarAsync("ESP32-002", "offline");

        Assert.Equal(0, await _e.RenovarCodigos().EjecutarAsync());

        _e.Reloj.Avanzar(TimeSpan.FromMinutes(10));
        Assert.Equal(1, await _e.RenovarCodigos().EjecutarAsync());
        Assert.Equal(2, _e.Comandos.De<MostrarQr>("ESP32-001").Count());
        Assert.Single(_e.Comandos.De<MostrarQr>("ESP32-002"));
    }

    [Fact]
    public async Task Con_escala_2_las_lecturas_siguientes_del_wearable_se_puntuan_con_esa_escala()
    {
        var ficha = await _e.IngresarAsync();
        await _e.VincularAsync(ficha.Id, _e.AgregarDispositivo());

        var cambio = await _e.CambiarEscalaSpo2().EjecutarAsync(new CambiarEscalaSpo2Comando(ficha.Id, "escala2", "EPOC con hipercapnia", Guid.NewGuid(), null));
        await _e.RegistrarLectura().EjecutarAsync(_e.Lectura(new SignosMedidos(Fc: 80, Spo2: 89, Temperatura: 36.8m), segundos: 1));

        Assert.True(cambio.EsExito);
        Assert.Contains("ESP32-001", _e.Resolutor.Invalidaciones);
        var ultima = _e.Historial.Evaluaciones.Last();
        Assert.Equal((OrigenEvaluacion.Lectura, EscalaSpo2.Escala2), (ultima.Origen, ultima.EscalaSpo2));
        Assert.Equal(0, ultima.News2().Puntos[ParametroClinico.Spo2]); // 89 % en escala 2 sin oxígeno = 0 (en escala 1 serían 3)
    }
}
