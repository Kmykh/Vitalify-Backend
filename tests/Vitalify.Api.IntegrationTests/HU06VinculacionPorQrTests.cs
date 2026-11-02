using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Vitalify.Api.IntegrationTests.Infraestructura;
using Vitalify.Application.Dispositivos;
using Vitalify.Application.Pacientes;
using Vitalify.Application.Vinculacion;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Dispositivos;
using Vitalify.Domain.Hospitalizaciones;
using Vitalify.Domain.Vinculacion;

namespace Vitalify.Api.IntegrationTests;

/// <summary>HU06 (fase 5) · Vinculación del wearable por QR y comandos al dispositivo (HU09).</summary>
public partial class HU06VinculacionPorQrTests(FabricaVitalify fabrica) : PruebaApi(fabrica)
{
    private const string MensajeQrNoValido = "El código QR no es válido o ya venció. Pida al sensor un código nuevo y vuelva a escanear.";

    /// <summary>Un sensor nuevo que se conecta: queda en línea y su pantalla muestra un QR.</summary>
    private async Task<DispositivoDto> SensorEnLineaAsync()
    {
        var sensor = await (await ClienteAdministradorAsync()).CrearDispositivoAsync();
        await Fabrica.SimularEstadoAsync(sensor.Codigo, "online");
        return sensor;
    }

    private static Task<HttpResponseMessage> EscanearAsync(HttpClient enfermera, string qr) =>
        enfermera.PostAsJsonAsync("/api/v1/vinculaciones/escaneo", new { qr });

    private static async Task<EscaneoQrDto> EscanearOkAsync(HttpClient enfermera, string qr)
    {
        var respuesta = await EscanearAsync(enfermera, qr);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<EscaneoQrDto>())!;
    }

    /// <summary>Toma inicial completa del asistente (oxígeno, conciencia por respuestas y dos lecturas de presión).</summary>
    private object ObservacionInicial() => new
    {
        versionGuia = "2026.11.1",
        oxigeno = new { recibe = false, objetivo88a92 = false },
        conciencia = new { despiertoCoherente = true, confusionNueva = false },
        presion = new
        {
            checklist = new { reposo5Min = true, brazoApoyadoAlturaCorazon = true, brazaleteTallaCorrecta = true, sinCafeTabacoEsfuerzo30Min = true, tensiometroValidado = true },
            posicion = "Sentado",
            brazo = "Izquierdo",
            metodo = "Automatico",
            lecturas = new[]
            {
                new { pas = 134, pad = 86, medidaEn = Fabrica.Reloj.AhoraUtc.AddMinutes(-3) },
                new { pas = 130, pad = 84, medidaEn = Fabrica.Reloj.AhoraUtc.AddMinutes(-1) },
            },
        },
        fr = 16,
    };

    private Task<HttpResponseMessage> ConfirmarAsync(HttpClient enfermera, string token, object paciente, Guid camaId) =>
        enfermera.PostAsJsonAsync("/api/v1/vinculaciones/confirmar", new
        {
            tokenVinculacion = token,
            paciente,
            camaId,
            diagnosticoIngreso = "Neumonía adquirida en la comunidad",
            observacionInicial = ObservacionInicial(),
        });

    private static object PacienteNuevo(string dni, string nombre = "Rosa Mendoza Quispe") => new
    {
        pacienteId = (Guid?)null,
        nombreCompleto = nombre,
        tipoDocumento = "Dni",
        numeroDocumento = dni,
        fechaNacimiento = "1958-04-12",
        confusionBasal = false,
    };

    [GeneratedRegex("""^\{"accion":"mostrar-qr","qr":"VTF1:(?<dispositivo>[A-Z0-9-]+):(?<codigo>[23456789ABCDEFGHJKMNPQRSTUVWXYZ]{8})","expira":"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z"\}$""")]
    private static partial Regex JsonMostrarQr();

    [Fact]
    public async Task QR_E1_UnWearableRegistradoYSinPacientePublicaOnline_RecibeMostrarQrConUnCodigoNuevo()
    {
        var sensor = await (await ClienteAdministradorAsync()).CrearDispositivoAsync();

        await Fabrica.SimularEstadoAsync(sensor.Codigo, "online");

        var comando = Assert.Single(Fabrica.Comandos.De(sensor.Codigo));
        Assert.Equal(($"device/{sensor.Codigo}/comandos", true), (comando.Topico, comando.Retenido));
        var json = JsonMostrarQr().Match(comando.Json);
        Assert.True(json.Success, comando.Json);
        Assert.Equal(sensor.Codigo, json.Groups["dispositivo"].Value);
        var mostrar = (MostrarQr)comando.Comando;
        Assert.Equal(Fabrica.Reloj.AhoraUtc.AddMinutes(10), mostrar.Expira, TimeSpan.FromSeconds(2));

        // En la base solo queda el hash del código, nunca el código en claro.
        var guardado = await Fabrica.ConsultarAsync(db => db.CodigosVinculacion.SingleAsync(c => c.DispositivoId == sensor.Id));
        Assert.Equal(CodigoVinculacion.Hash(json.Groups["codigo"].Value), guardado.HashCodigo);
        Assert.DoesNotContain(json.Groups["codigo"].Value, guardado.HashCodigo, StringComparison.OrdinalIgnoreCase);
        Assert.True(await Fabrica.ConsultarAsync(db => db.Auditoria.AnyAsync(a => a.Accion == AccionAuditoria.QrGenerado && a.Detalle!.Contains(sensor.Id.ToString()))));

        // Cada conexión trae un código nuevo y el anterior deja de estar activo.
        await Fabrica.SimularEstadoAsync(sensor.Codigo, "online");
        Assert.NotEqual(mostrar.Qr, Fabrica.Comandos.UltimoQr(sensor.Codigo));
        var codigos = await Fabrica.ConsultarAsync(db => db.CodigosVinculacion.Where(c => c.DispositivoId == sensor.Id).ToListAsync());
        Assert.Equal(1, codigos.Count(c => c.EstaActivo));
    }

    [Fact]
    public async Task QR_E2_LaEnfermeraEscaneaUnQrValidoConElSensorEnLinea_ConexionEstablecidaYElWearableEmpareja()
    {
        var sensor = await SensorEnLineaAsync();
        var enfermera = await ClienteEnfermeraAsync();

        var escaneo = await EscanearOkAsync(enfermera, Fabrica.Comandos.UltimoQr(sensor.Codigo));

        Assert.Equal(("establecida", sensor.Id, sensor.Codigo), (escaneo.Conexion, escaneo.Dispositivo.Id, escaneo.Dispositivo.Codigo));
        Assert.False(string.IsNullOrWhiteSpace(escaneo.TokenVinculacion));
        Assert.Equal(Fabrica.Reloj.AhoraUtc.AddMinutes(5), escaneo.ExpiraEn, TimeSpan.FromSeconds(2));

        var emparejando = Fabrica.Comandos.De(sensor.Codigo).Last();
        Assert.Equal(("""{"accion":"emparejando"}""", false), (emparejando.Json, emparejando.Retenido));

        var codigo = await Fabrica.ConsultarAsync(db => db.CodigosVinculacion.SingleAsync(c => c.DispositivoId == sensor.Id && c.UsadoEn == null && c.InvalidadoEn == null));
        Assert.Equal(CodigoVinculacion.Hash(escaneo.TokenVinculacion), codigo.TokenHash);
        Assert.True(await Fabrica.ConsultarAsync(db => db.Auditoria.AnyAsync(a => a.Accion == AccionAuditoria.QrEscaneado && a.Detalle!.Contains(sensor.Id.ToString()))));
    }

    [Fact]
    public async Task QR_E3_QrVencidoUsadoOInventado_Devuelve404ConElMismoMensaje()
    {
        var administrador = await ClienteAdministradorAsync();
        var enfermera = await ClienteEnfermeraAsync();
        var cama = await administrador.CrearCamaAsync();

        // Ya usado: se confirma la vinculación y se vuelve a escanear el mismo QR.
        var usado = await SensorEnLineaAsync();
        var qrUsado = Fabrica.Comandos.UltimoQr(usado.Codigo);
        var escaneo = await EscanearOkAsync(enfermera, qrUsado);
        Assert.Equal(HttpStatusCode.Created, (await ConfirmarAsync(enfermera, escaneo.TokenVinculacion, PacienteNuevo(ClienteApi.NuevoDni()), cama.Id)).StatusCode);

        // Inventado: un código con el formato correcto que nunca se generó, y un texto cualquiera.
        var otro = await SensorEnLineaAsync();
        var inventado = $"VTF1:{otro.Codigo}:{(Fabrica.Comandos.UltimoQr(otro.Codigo).EndsWith(":22222222", StringComparison.Ordinal) ? "33333333" : "22222222")}";

        // Vencido: pasan más de 10 minutos.
        var vencido = await SensorEnLineaAsync();
        var qrVencido = Fabrica.Comandos.UltimoQr(vencido.Codigo);

        var respuestas = new List<HttpResponseMessage>
        {
            await EscanearAsync(enfermera, qrUsado),
            await EscanearAsync(enfermera, inventado),
            await EscanearAsync(enfermera, "hola"),
        };
        using (Fabrica.Reloj.Adelantar(TimeSpan.FromMinutes(11)))
        {
            respuestas.Add(await EscanearAsync(enfermera, qrVencido));
        }

        foreach (var respuesta in respuestas)
        {
            Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
            var problema = await respuesta.LeerProblemaAsync();
            Assert.Equal(("qr-no-valido", MensajeQrNoValido), (problema.Type, problema.Detail));
        }
    }

    [Fact]
    public async Task QR_E3_AlQuintoIntentoFallidoSobreElMismoDispositivo_SuCodigoSeInvalidaYSePublicaUnoNuevo()
    {
        var sensor = await SensorEnLineaAsync();
        var enfermera = await ClienteEnfermeraAsync();
        var qrOriginal = Fabrica.Comandos.UltimoQr(sensor.Codigo);
        var equivocado = $"VTF1:{sensor.Codigo}:{(qrOriginal.EndsWith(":22222222", StringComparison.Ordinal) ? "33333333" : "22222222")}";

        for (var intento = 1; intento <= 5; intento++)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await EscanearAsync(enfermera, equivocado)).StatusCode);
            Assert.Equal(intento < 5 ? 1 : 2, Fabrica.Comandos.De(sensor.Codigo).Count(c => c.Comando is MostrarQr));
        }

        Assert.NotEqual(qrOriginal, Fabrica.Comandos.UltimoQr(sensor.Codigo));
        Assert.Equal(5, await Fabrica.ConsultarAsync(db => db.Auditoria.CountAsync(a =>
            a.Accion == AccionAuditoria.QrRechazado && a.Detalle!.Contains(sensor.Id.ToString()) && a.Detalle.Contains("código incorrecto"))));

        // El QR que se mostraba ya no sirve; el nuevo, sí.
        Assert.Equal(HttpStatusCode.NotFound, (await EscanearAsync(enfermera, qrOriginal)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await EscanearAsync(enfermera, Fabrica.Comandos.UltimoQr(sensor.Codigo))).StatusCode);
    }

    [Fact]
    public async Task QR_E4_SensorConOfflineOSinPresencia_Devuelve409SensorSinConexion()
    {
        var enfermera = await ClienteEnfermeraAsync();
        var administrador = await ClienteAdministradorAsync();

        // Se conectó (tiene QR) y el broker publicó su "last will".
        var desconectado = await SensorEnLineaAsync();
        await Fabrica.SimularEstadoAsync(desconectado.Codigo, "offline");

        // Nunca se conectó: el QR se generó a mano.
        var sinPresencia = await administrador.CrearDispositivoAsync();
        Assert.Equal(HttpStatusCode.OK, (await administrador.PostAsync($"/api/v1/dispositivos/{sinPresencia.Id}/qr", null)).StatusCode);

        foreach (var sensor in new[] { desconectado, sinPresencia })
        {
            var respuesta = await EscanearAsync(enfermera, Fabrica.Comandos.UltimoQr(sensor.Codigo));
            Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
            Assert.Equal("sensor-sin-conexion", (await respuesta.LeerProblemaAsync()).Type);
        }
    }

    [Fact]
    public async Task QR_E5_ConfirmarConPacienteNuevoCamaLibreYObservacionInicial_PacienteHospitalizadoVinculadoYPrimerRiesgo()
    {
        var sensor = await SensorEnLineaAsync();
        var cama = await (await ClienteAdministradorAsync()).CrearCamaAsync();
        var enfermera = await ClienteEnfermeraAsync();
        var dni = ClienteApi.NuevoDni();
        var escaneo = await EscanearOkAsync(enfermera, Fabrica.Comandos.UltimoQr(sensor.Codigo));

        var respuesta = await ConfirmarAsync(enfermera, escaneo.TokenVinculacion, PacienteNuevo(dni), cama.Id);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var cuerpo = (await respuesta.Content.ReadFromJsonAsync<VinculacionConfirmadaDto>())!;
        Assert.Equal($"/api/v1/pacientes/{cuerpo.Paciente.Id}", respuesta.Headers.Location?.OriginalString);
        Assert.Equal(("Rosa Mendoza Quispe", dni), (cuerpo.Paciente.NombreCompleto, cuerpo.Paciente.NumeroDocumento));
        var activa = cuerpo.Paciente.HospitalizacionActiva!;
        Assert.Equal((cama.Codigo, sensor.Codigo), (activa.Cama.Codigo, activa.Dispositivo!.Codigo));
        Assert.Equal(sensor.Id, cuerpo.Dispositivo.Id);
        Assert.Empty(cuerpo.Avisos);

        // Primer NEWS2/MEWS con la observación inicial: presión promediada (132/85), A, aire y FR 16.
        var riesgo = cuerpo.Riesgo!;
        Assert.Equal(("Observacion", (int?)132, "Alerta", (bool?)false, (int?)16),
            (riesgo.Origen, riesgo.Parametros.Pas, riesgo.Parametros.Conciencia, riesgo.Parametros.OxigenoSuplementario, riesgo.Parametros.Fr));
        Assert.Equal(["spo2", "fc", "temperatura"], riesgo.News2.Faltantes);

        var hospitalizacion = await Fabrica.ConsultarAsync(db => db.Hospitalizaciones.SingleAsync(h => h.Id == activa.Id));
        Assert.Equal(EstadoHospitalizacion.Activa, hospitalizacion.Estado);
        Assert.Equal(EstadoDispositivo.Asignado, (await Fabrica.ConsultarAsync(db => db.Dispositivos.SingleAsync(d => d.Id == sensor.Id))).Estado);
        Assert.NotNull((await Fabrica.ConsultarAsync(db => db.CodigosVinculacion.SingleAsync(c => c.TokenHash == CodigoVinculacion.Hash(escaneo.TokenVinculacion)))).UsadoEn);

        var observacion = await Fabrica.ConsultarHistorialAsync(db => db.Observaciones.SingleAsync(o => o.HospitalizacionId == activa.Id));
        Assert.Equal(((int?)132, (int?)85, 2, "2026.11.1"), (observacion.Pas, observacion.Pad, observacion.LecturasPa!.Count, observacion.VersionGuia));
        Assert.True(await Fabrica.ConsultarHistorialAsync(db => db.Evaluaciones.AnyAsync(e => e.HospitalizacionId == activa.Id)));

        // El wearable recibe "vinculado" con la cama y las iniciales: nada más del paciente.
        var vinculado = Fabrica.Comandos.De(sensor.Codigo).Last();
        Assert.Equal(($$"""{"accion":"vinculado","cama":"{{cama.Codigo}}","iniciales":"R.M."}""", true), (vinculado.Json, vinculado.Retenido));

        var auditoria = await Fabrica.ConsultarAsync(db => db.Auditoria
            .Where(a => a.Detalle!.Contains(activa.Id.ToString()) || a.Detalle.Contains(sensor.Id.ToString())).ToListAsync());
        Assert.Contains(auditoria, a => a.Accion == AccionAuditoria.VinculacionPorQr);
        Assert.Contains(auditoria, a => a.Accion == AccionAuditoria.PacienteIngresado);
        Assert.Contains(auditoria, a => a.Accion == AccionAuditoria.ObservacionRegistrada);
        Assert.All(auditoria, a => Assert.DoesNotContain(dni, a.Detalle!));
        Assert.All(auditoria, a => Assert.DoesNotContain("Mendoza", a.Detalle!));

        // El token es de un solo uso.
        var repetido = await ConfirmarAsync(enfermera, escaneo.TokenVinculacion, PacienteNuevo(ClienteApi.NuevoDni()), (await (await ClienteAdministradorAsync()).CrearCamaAsync()).Id);
        Assert.Equal("vinculacion-no-valida", (await repetido.LeerProblemaAsync()).Type);
    }

    [Fact]
    public async Task QR_E5_ConObservacionInicialIncompleta_400YNoSeGuardaNada()
    {
        var sensor = await SensorEnLineaAsync();
        var cama = await (await ClienteAdministradorAsync()).CrearCamaAsync();
        var enfermera = await ClienteEnfermeraAsync();
        var dni = ClienteApi.NuevoDni();
        var escaneo = await EscanearOkAsync(enfermera, Fabrica.Comandos.UltimoQr(sensor.Codigo));

        var respuesta = await enfermera.PostAsJsonAsync("/api/v1/vinculaciones/confirmar", new
        {
            tokenVinculacion = escaneo.TokenVinculacion,
            paciente = PacienteNuevo(dni),
            camaId = cama.Id,
            diagnosticoIngreso = "Neumonía",
            observacionInicial = new { fr = 18 },
        });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var problema = (await respuesta.Content.ReadFromJsonAsync<ValidationProblemDetails>())!;
        Assert.Contains("observacionInicial.oxigeno", problema.Errors.Keys);
        Assert.Contains("observacionInicial.conciencia", problema.Errors.Keys);
        Assert.Contains("observacionInicial.presion", problema.Errors.Keys);
        Assert.False(await Fabrica.ConsultarAsync(db => db.Pacientes.AnyAsync(p => p.NumeroDocumento == dni)));
        Assert.Equal(EstadoDispositivo.Disponible, (await Fabrica.ConsultarAsync(db => db.Dispositivos.SingleAsync(d => d.Id == sensor.Id))).Estado);
    }

    [Fact]
    public async Task QR_E6_ConfirmarConUnPacienteQueYaExisteYSinHospitalizacionActiva_SeReutilizaElPaciente()
    {
        var administrador = await ClienteAdministradorAsync();
        var enfermera = await ClienteEnfermeraAsync();
        var dni = ClienteApi.NuevoDni();

        // Estuvo hospitalizado antes y egresó.
        var anterior = await enfermera.IngresarPacienteAsync((await administrador.CrearCamaAsync()).Id, dni);
        Assert.Equal(HttpStatusCode.OK, (await enfermera.EgresarAsync(anterior.Id)).StatusCode);

        var sensor = await SensorEnLineaAsync();
        var escaneo = await EscanearOkAsync(enfermera, Fabrica.Comandos.UltimoQr(sensor.Codigo));
        var respuesta = await ConfirmarAsync(enfermera, escaneo.TokenVinculacion, PacienteNuevo(dni, "Otro Nombre Distinto"), (await administrador.CrearCamaAsync()).Id);

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var cuerpo = (await respuesta.Content.ReadFromJsonAsync<VinculacionConfirmadaDto>())!;
        Assert.Equal((anterior.Id, anterior.NombreCompleto), (cuerpo.Paciente.Id, cuerpo.Paciente.NombreCompleto));
        Assert.Equal(1, await Fabrica.ConsultarAsync(db => db.Pacientes.CountAsync(p => p.NumeroDocumento == dni)));
        Assert.Equal(2, await Fabrica.ConsultarAsync(db => db.Hospitalizaciones.CountAsync(h => h.PacienteId == anterior.Id)));

        // Con pacienteId de alguien que ya está hospitalizado: 409.
        var otroSensor = await SensorEnLineaAsync();
        var otroEscaneo = await EscanearOkAsync(enfermera, Fabrica.Comandos.UltimoQr(otroSensor.Codigo));
        var conflicto = await ConfirmarAsync(enfermera, otroEscaneo.TokenVinculacion, new { pacienteId = anterior.Id }, (await administrador.CrearCamaAsync()).Id);
        Assert.Equal(HttpStatusCode.Conflict, conflicto.StatusCode);
        Assert.Equal("paciente-ya-hospitalizado", (await conflicto.LeerProblemaAsync()).Type);
    }

    [Fact]
    public async Task QR_E7_ElEgresoLiberaElSensor_SePublicaDesvinculadoYLuegoUnMostrarQrNuevo()
    {
        var sensor = await SensorEnLineaAsync();
        var enfermera = await ClienteEnfermeraAsync();
        var escaneo = await EscanearOkAsync(enfermera, Fabrica.Comandos.UltimoQr(sensor.Codigo));
        var confirmada = (await (await ConfirmarAsync(enfermera, escaneo.TokenVinculacion, PacienteNuevo(ClienteApi.NuevoDni()),
            (await (await ClienteAdministradorAsync()).CrearCamaAsync()).Id)).Content.ReadFromJsonAsync<VinculacionConfirmadaDto>())!;
        var antes = Fabrica.Comandos.De(sensor.Codigo).Count;

        Assert.Equal(HttpStatusCode.OK, (await enfermera.EgresarAsync(confirmada.Paciente.Id)).StatusCode);

        var nuevos = Fabrica.Comandos.De(sensor.Codigo).Skip(antes).ToList();
        Assert.Equal(["desvinculado", "mostrar-qr"], nuevos.Select(c => c.Comando.Accion));
        Assert.All(nuevos, c => Assert.True(c.Retenido));
        Assert.Equal("""{"accion":"desvinculado"}""", nuevos[0].Json);
        Assert.Equal(HttpStatusCode.OK, (await EscanearAsync(enfermera, Fabrica.Comandos.UltimoQr(sensor.Codigo))).StatusCode);
    }

    [Fact]
    public async Task AlLiberarElSensorManualmente_SePublicaDesvinculadoYUnQrNuevo()
    {
        var (enfermera, ficha, sensor, _) = await PacienteConSensorAsync();
        var antes = Fabrica.Comandos.De(sensor.Codigo).Count;

        Assert.Equal(HttpStatusCode.NoContent, (await enfermera.DeleteAsync($"/api/v1/pacientes/{ficha.Id}/dispositivo")).StatusCode);

        Assert.Equal(["desvinculado", "mostrar-qr"], Fabrica.Comandos.AccionesDe(sensor.Codigo).Skip(antes));
    }

    [Fact]
    public async Task LaVinculacionManualDeRespaldo_PublicaVinculadoEInvalidaElQrDelSensor()
    {
        var administrador = await ClienteAdministradorAsync();
        var enfermera = await ClienteEnfermeraAsync();
        var sensor = await SensorEnLineaAsync();
        var qr = Fabrica.Comandos.UltimoQr(sensor.Codigo);
        var cama = await administrador.CrearCamaAsync();
        var ficha = await enfermera.IngresarPacienteAsync(cama.Id);

        Assert.Equal(HttpStatusCode.OK, (await enfermera.VincularAsync(ficha.Id, sensor.Id)).StatusCode);

        var vinculado = Fabrica.Comandos.De(sensor.Codigo).Last();
        Assert.Equal(($$"""{"accion":"vinculado","cama":"{{cama.Codigo}}","iniciales":"P.P."}""", true), (vinculado.Json, vinculado.Retenido));
        Assert.Equal(HttpStatusCode.NotFound, (await EscanearAsync(enfermera, qr)).StatusCode);

        // Si el ESP32 se reinicia, al volver a conectarse recibe "vinculado" otra vez, no un QR.
        await Fabrica.SimularEstadoAsync(sensor.Codigo, "online");
        Assert.Equal("vinculado", Fabrica.Comandos.De(sensor.Codigo).Last().Comando.Accion);
    }

    [Fact]
    public async Task RegenerarQr_LoPuedenPedirAdministradorYPersonalClinico_PeroNoSiElSensorEstaVinculado()
    {
        var (enfermera, _, vinculado, _) = await PacienteConSensorAsync();
        var libre = await (await ClienteAdministradorAsync()).CrearDispositivoAsync();

        var respuesta = await enfermera.PostAsync($"/api/v1/dispositivos/{libre.Id}/qr", null);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var qr = (await respuesta.Content.ReadFromJsonAsync<QrGeneradoDto>())!;
        Assert.Equal((libre.Id, libre.Codigo), (qr.DispositivoId, qr.CodigoDispositivo));
        Assert.DoesNotContain("VTF1", await respuesta.Content.ReadAsStringAsync());

        var conflicto = await (await ClienteAdministradorAsync()).PostAsync($"/api/v1/dispositivos/{vinculado.Id}/qr", null);
        Assert.Equal(HttpStatusCode.Conflict, conflicto.StatusCode);
        Assert.Equal("dispositivo-ya-vinculado", (await conflicto.LeerProblemaAsync()).Type);
    }

    [Fact]
    public async Task SoloLaEnfermeraEscaneaYConfirma()
    {
        var sensor = await SensorEnLineaAsync();
        var qr = Fabrica.Comandos.UltimoQr(sensor.Codigo);

        Assert.Equal(HttpStatusCode.Forbidden, (await EscanearAsync(await ClienteMedicoAsync(), qr)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await EscanearAsync(await ClienteAdministradorAsync(), qr)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await ClienteMedicoAsync()).PostAsJsonAsync("/api/v1/vinculaciones/confirmar", new { })).StatusCode);
    }

    [Fact]
    public async Task ElWorkerRenuevaLosQrVencidosDeLosSensoresEnLineaYRespetaUnEscaneoEnCurso()
    {
        var vencido = await SensorEnLineaAsync();
        var emparejando = await SensorEnLineaAsync();
        var enfermera = await ClienteEnfermeraAsync();

        using (Fabrica.Reloj.Adelantar(TimeSpan.FromMinutes(8)))
        {
            await EscanearOkAsync(enfermera, Fabrica.Comandos.UltimoQr(emparejando.Codigo));
        }

        var qrsAntes = (Vencido: Fabrica.Comandos.UltimoQr(vencido.Codigo), Emparejando: Fabrica.Comandos.UltimoQr(emparejando.Codigo));
        using (Fabrica.Reloj.Adelantar(TimeSpan.FromMinutes(11)))
        {
            Assert.True(await Fabrica.RenovarCodigosAsync() >= 1);
        }

        Assert.NotEqual(qrsAntes.Vencido, Fabrica.Comandos.UltimoQr(vencido.Codigo));
        Assert.Equal(qrsAntes.Emparejando, Fabrica.Comandos.UltimoQr(emparejando.Codigo));
    }
}
