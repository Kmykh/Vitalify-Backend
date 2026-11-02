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
