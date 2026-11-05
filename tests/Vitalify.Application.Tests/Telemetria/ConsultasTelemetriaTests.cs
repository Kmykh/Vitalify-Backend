using Vitalify.Application.Comun;
using Vitalify.Application.Telemetria;
using Vitalify.Application.Tests.Fakes;

namespace Vitalify.Application.Tests.Telemetria;

public class ConsultasTelemetriaTests
{
    private readonly Escenario _e = new();

    private async Task<Guid> PacienteConSensorAsync()
    {
        var ficha = await _e.IngresarAsync();
        await _e.VincularAsync(ficha.Id, _e.AgregarDispositivo());
        return ficha.Id;
    }

    [Fact]
    public async Task Los_signos_actuales_marcan_pendiente_la_variable_que_dejo_de_llegar()
    {
        var pacienteId = await PacienteConSensorAsync();
        await _e.RegistrarLectura().EjecutarAsync(_e.Lectura(new(Temperatura: 37.1m, Spo2: 97)));
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(100));
        await _e.RegistrarLectura().EjecutarAsync(_e.Lectura(new(Spo2: 96)));

        var signos = (await _e.ObtenerSignosActuales().EjecutarAsync(pacienteId)).Valor;

        Assert.Equal((37.1m, "pendiente-actualizacion"), (signos.Temperatura.Valor, signos.Temperatura.Estado));
        Assert.Equal((96m, "vigente"), (signos.Spo2.Valor, signos.Spo2.Estado));
        Assert.Equal("sin-datos", signos.Fc.Estado);
        Assert.Equal(("ESP32-001", "con-datos"), (signos.CodigoDispositivo, signos.Senal));
    }

    [Fact]
    public async Task Sin_lecturas_todo_queda_sin_datos()
    {
        var pacienteId = await PacienteConSensorAsync();

        var signos = (await _e.ObtenerSignosActuales().EjecutarAsync(pacienteId)).Valor;

        Assert.All(new[] { signos.Fc, signos.Fr, signos.Spo2, signos.Temperatura, signos.Pas, signos.Pad }, v => Assert.Equal("sin-datos", v.Estado));
        Assert.Equal("sin-datos", signos.Senal);
    }

    [Fact]
    public async Task Sin_hospitalizacion_activa_los_signos_no_se_encuentran()
    {
        var pacienteId = await PacienteConSensorAsync();
        await _e.RegistrarEgreso().EjecutarAsync(new(pacienteId, "AltaMedica", null, _e.EnfermeraId, null));

        var resultado = await _e.ObtenerSignosActuales().EjecutarAsync(pacienteId);

        Assert.Equal((TipoError.NoEncontrado, "sin-hospitalizacion-activa"), (resultado.Error.Tipo, resultado.Error.Codigo));
    }

    [Fact]
    public async Task El_historial_por_defecto_trae_el_episodio_completo_en_orden_cronologico()
    {
        var pacienteId = await _e.HaceAsync(TimeSpan.FromHours(2), PacienteConSensorAsync);
        await _e.RegistrarLectura().EjecutarAsync(_e.Lectura(new(Fc: 70), segundos: -90 * 60));
        await _e.RegistrarLectura().EjecutarAsync(_e.Lectura(new(Fc: 82, Spo2: 97), segundos: -10));
        await _e.RegistrarLectura().EjecutarAsync(_e.Lectura(new(Fc: 78), segundos: -20 * 60, caida: true));

        var historial = (await _e.ObtenerHistorialSignos().EjecutarAsync(new(pacienteId))).Valor;

        Assert.Equal([70, 78, 82], historial.Lecturas.Select(l => l.Fc));
        Assert.True(historial.Lecturas[1].Caida);
        Assert.Equal((97, "ApiDesarrollo"), (historial.Lecturas[2].Spo2, historial.Lecturas[2].Origen));
        Assert.Equal((1L, TimeSpan.FromSeconds(10)), (historial.Lecturas[2].Seq, historial.Lecturas[2].RecibidoEn - historial.Lecturas[2].MedidoEn));
        Assert.Equal((_e.Reloj.AhoraUtc.AddHours(-2), _e.Reloj.AhoraUtc), (historial.Desde, historial.Hasta));
        Assert.Null(historial.Grupos);
    }

    [Fact]
    public async Task El_historial_rechaza_un_rango_invertido_o_de_mas_de_7_dias_sin_agrupar()
    {
        // Un episodio de 10 días: sin agrupar no se puede pedir completo; agrupado, sí.
        var pacienteId = await _e.HaceAsync(TimeSpan.FromDays(10), PacienteConSensorAsync);
        var ahora = _e.Reloj.AhoraUtc;

        var invertido = await _e.ObtenerHistorialSignos().EjecutarAsync(new(pacienteId, ahora, ahora.AddHours(-1)));
        var largo = await _e.ObtenerHistorialSignos().EjecutarAsync(new(pacienteId, ahora.AddDays(-7).AddSeconds(-1), ahora));
        var episodioSinAgrupar = await _e.ObtenerHistorialSignos().EjecutarAsync(new(pacienteId));
        var agrupado = await _e.ObtenerHistorialSignos().EjecutarAsync(new(pacienteId, Agrupar: "1h"));
        var agrupadoInvalido = await _e.ObtenerHistorialSignos().EjecutarAsync(new(pacienteId, Agrupar: "2h"));

        Assert.Contains("hasta", invertido.Error.Detalles.Keys);
        Assert.Contains("desde", largo.Error.Detalles.Keys);
        Assert.Contains("agrupar", episodioSinAgrupar.Error.Detalles.Keys);
        Assert.Equal((TimeSpan.FromDays(10), "1h"), (agrupado.Valor.Hasta - agrupado.Valor.Desde, agrupado.Valor.Agrupar));
        Assert.Contains("agrupar", agrupadoInvalido.Error.Detalles.Keys);
    }

    [Fact]
    public async Task Sin_hospitalizacion_activa_el_historial_es_el_de_la_ultima_y_un_paciente_nunca_hospitalizado_da_404()
    {
        var pacienteId = await PacienteConSensorAsync();
        await _e.RegistrarLectura().EjecutarAsync(_e.Lectura(new(Fc: 80)));
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(5));
        await _e.RegistrarEgreso().EjecutarAsync(new(pacienteId, "AltaMedica", null, _e.EnfermeraId, null));
        var nuncaHospitalizado = Domain.Pacientes.Paciente.Registrar("Ana Torres", Domain.Pacientes.TipoDocumento.Dni, "71234567", new DateOnly(1990, 1, 1), false, _e.Reloj.AhoraUtc);
        _e.Pacientes.Agregar(nuncaHospitalizado);

        var ultima = await _e.ObtenerHistorialSignos().EjecutarAsync(new(pacienteId));
        var nunca = await _e.ObtenerHistorialSignos().EjecutarAsync(new(nuncaHospitalizado.Id));

        Assert.Equal(80, Assert.Single(ultima.Valor.Lecturas).Fc);
        Assert.Equal((TipoError.NoEncontrado, "paciente-sin-hospitalizaciones"), (nunca.Error.Tipo, nunca.Error.Codigo));
    }

    [Fact]
    public async Task Monitoreados_indica_la_senal_de_cada_paciente()
    {
        await PacienteConSensorAsync();

        var sinDatos = (await _e.ListarPacientesMonitoreados().EjecutarAsync()).Valor.Items.Single();
        await _e.RegistrarLectura().EjecutarAsync(_e.Lectura(new(Fc: 80)));
        var conDatos = (await _e.ListarPacientesMonitoreados().EjecutarAsync()).Valor.Items.Single();
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(181));
        var sinSenal = (await _e.ListarPacientesMonitoreados().EjecutarAsync()).Valor.Items.Single();

        Assert.Equal(("sin-datos", null), (sinDatos.Senal, sinDatos.UltimaLecturaEn));
        Assert.Equal("con-datos", conDatos.Senal);
        Assert.NotNull(conDatos.UltimaLecturaEn);
        Assert.Equal("sin-senal", sinSenal.Senal);
    }

    [Fact]
    public async Task Las_incidencias_se_filtran_por_tipo_y_se_validan_los_filtros()
    {
        await PacienteConSensorAsync();
        await _e.RegistrarLectura().EjecutarAsync(_e.Lectura(new(Fc: 400, Fr: 16)));
        await _e.RegistrarLectura().EjecutarAsync(_e.Lectura(new(Pas: 118), segundos: 1));

        var presion = await _e.ListarIncidencias().EjecutarAsync(new(Tipo: "presionincompleta"));
        var tipoInvalido = await _e.ListarIncidencias().EjecutarAsync(new(Tipo: "Otro"));
        var rangoInvalido = await _e.ListarIncidencias().EjecutarAsync(new(Desde: DateTime.UtcNow, Hasta: DateTime.UtcNow.AddDays(-1)));

        Assert.Equal("PresionIncompleta", Assert.Single(presion.Valor.Elementos).Tipo);
        Assert.Contains("tipo", tipoInvalido.Error.Detalles.Keys);
        Assert.Contains("hasta", rangoInvalido.Error.Detalles.Keys);
    }
}
