using Vitalify.Application.Comun;
using Vitalify.Application.Tests.Fakes;
using Vitalify.Domain.Dispositivos;

namespace Vitalify.Application.Tests.Dispositivos;

public class CamasYDispositivosTests
{
    private readonly Escenario _e = new();

    [Fact]
    public async Task Registrar_cama_normaliza_y_rechaza_codigos_repetidos()
    {
        var creada = await _e.RegistrarCama().EjecutarAsync(new(" med-b-07 ", "Medicina B"));
        var repetida = await _e.RegistrarCama().EjecutarAsync(new("MED-B-07", "Medicina B"));

        Assert.Equal("MED-B-07", creada.Valor.Codigo);
        Assert.Equal("cama-codigo-duplicado", repetida.Error.Codigo);
    }

    [Fact]
    public async Task Solo_disponibles_excluye_camas_ocupadas_e_inactivas()
    {
        await _e.IngresarAsync("MED-B-01");
        _e.AgregarCama("MED-B-02");
        _e.AgregarCama("MED-B-03").Desactivar();

        var disponibles = await _e.ListarCamas().EjecutarAsync(soloDisponibles: true);
        var todas = await _e.ListarCamas().EjecutarAsync(soloDisponibles: false);

        Assert.Equal(["MED-B-02"], disponibles.Valor.Select(c => c.Codigo));
        Assert.True(todas.Valor.Single(c => c.Codigo == "MED-B-01").Ocupada);
    }

    [Theory]
    [InlineData("ES")]
    [InlineData("ESP32 001")]
    public async Task Registrar_dispositivo_valida_el_codigo(string codigo)
    {
        var resultado = await _e.RegistrarDispositivo().EjecutarAsync(new(codigo, null));

        Assert.Contains("codigo", resultado.Error.Detalles.Keys);
    }

    [Fact]
    public async Task Registrar_dispositivo_rechaza_codigos_repetidos()
    {
        await _e.RegistrarDispositivo().EjecutarAsync(new("ESP32-001", "Pulsera 1"));

        var resultado = await _e.RegistrarDispositivo().EjecutarAsync(new("esp32-001", null));

        Assert.Equal("dispositivo-codigo-duplicado", resultado.Error.Codigo);
    }

    [Fact]
    public async Task Cambiar_estado_sigue_la_maquina_de_estados()
    {
        var sensor = _e.AgregarDispositivo();

        var mantenimiento = await _e.CambiarEstadoDispositivo().EjecutarAsync(new(sensor.Id, "mantenimiento"));
        var otraVez = await _e.CambiarEstadoDispositivo().EjecutarAsync(new(sensor.Id, "Mantenimiento"));
        var reactivado = await _e.CambiarEstadoDispositivo().EjecutarAsync(new(sensor.Id, "Disponible"));

        Assert.Equal("Mantenimiento", mantenimiento.Valor.Estado);
        Assert.True(otraVez.EsExito);
        Assert.Equal("Disponible", reactivado.Valor.Estado);
    }

    [Fact]
    public async Task Un_sensor_asignado_no_cambia_de_estado_por_la_via_administrativa()
    {
        var ficha = await _e.IngresarAsync();
        var sensor = _e.AgregarDispositivo();
        await _e.VincularAsync(ficha.Id, sensor);

        var resultado = await _e.CambiarEstadoDispositivo().EjecutarAsync(new(sensor.Id, "DadoDeBaja"));

        Assert.Equal((TipoError.Conflicto, "transicion-invalida"), (resultado.Error.Tipo, resultado.Error.Codigo));
        Assert.Equal(EstadoDispositivo.Asignado, sensor.Estado);
    }

    [Theory]
    [InlineData("Asignado")]
    [InlineData("2")]
    [InlineData("Roto")]
    public async Task Cambiar_estado_solo_acepta_estados_administrativos(string estado)
    {
        var sensor = _e.AgregarDispositivo();

        var resultado = await _e.CambiarEstadoDispositivo().EjecutarAsync(new(sensor.Id, estado));

        Assert.Contains("estado", resultado.Error.Detalles.Keys);
    }

    [Fact]
    public async Task Cambiar_estado_de_un_dispositivo_inexistente_es_no_encontrado()
    {
        var resultado = await _e.CambiarEstadoDispositivo().EjecutarAsync(new(Guid.NewGuid(), "Mantenimiento"));

        Assert.Equal(TipoError.NoEncontrado, resultado.Error.Tipo);
    }

    [Fact]
    public async Task Listar_dispositivos_filtra_por_estado_y_muestra_la_cama_del_asignado()
    {
        var ficha = await _e.IngresarAsync();
        var asignado = _e.AgregarDispositivo("ESP32-001");
        _e.AgregarDispositivo("ESP32-002");
        await _e.VincularAsync(ficha.Id, asignado);

        var asignados = await _e.ListarDispositivos().EjecutarAsync("asignado");
        var invalido = await _e.ListarDispositivos().EjecutarAsync("Perdido");

        var item = Assert.Single(asignados.Valor);
        Assert.Equal(("ESP32-001", "MED-B-01"), (item.Codigo, item.CodigoCama));
        Assert.Contains("estado", invalido.Error.Detalles.Keys);
    }
}
