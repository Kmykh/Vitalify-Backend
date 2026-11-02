using Vitalify.Application.Comun;
using Vitalify.Application.Telemetria;
using Vitalify.Application.Tests.Fakes;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Application.Tests.Telemetria;

public class RegistrarLecturaTests
{
    private readonly Escenario _e = new();

    private async Task<Guid> PacienteConSensorAsync(string codigo = "ESP32-001")
    {
        var ficha = await _e.IngresarAsync();
        await _e.VincularAsync(ficha.Id, _e.AgregarDispositivo(codigo));
        return ficha.Id;
    }

    private Task<Resultado<ResultadoIngesta>> Registrar(LecturaTelemetria lectura) => _e.RegistrarLectura().EjecutarAsync(lectura);

    [Fact]
    public async Task Una_lectura_valida_se_guarda_asociada_a_la_hospitalizacion_y_actualiza_el_estado()
    {
        var pacienteId = await PacienteConSensorAsync();

        var resultado = await Registrar(_e.Lectura(new(Fc: 82, Fr: 16, Spo2: 97, Temperatura: 36.8m, Pas: 118, Pad: 76, Bateria: 87)));

        Assert.Equal("Aceptada", resultado.Valor.Estado);
        Assert.Empty(resultado.Valor.Incidencias);
        var lectura = Assert.Single(_e.Historial.Lecturas);
        var hospitalizacion = _e.Hospitalizaciones.Hospitalizaciones.Single();
        Assert.Equal((hospitalizacion.Id, pacienteId, 82, OrigenLectura.ApiDesarrollo), (lectura.HospitalizacionId, lectura.PacienteId, lectura.Fc, lectura.Origen));
        Assert.Equal(_e.Reloj.AhoraUtc, lectura.MedidoEn);
        var estado = Assert.Single(_e.Historial.Estados);
        Assert.Equal((82, 36.8m, 87), (estado.FcValor, estado.TempValor, estado.Bateria));
        Assert.Equal(hospitalizacion.Id, Assert.Single(_e.Manejador.Eventos).HospitalizacionId);
    }

    [Fact]
    public async Task Una_variable_imposible_se_descarta_sola_y_queda_como_incidencia()
    {
        await PacienteConSensorAsync();

        var resultado = await Registrar(_e.Lectura(new(Fc: 400, Fr: 16)));

        Assert.Equal("AceptadaParcial", resultado.Valor.Estado);
        var incidencia = Assert.Single(_e.Historial.Incidencias);
        Assert.Equal((TipoIncidencia.FueraDeRango, VariableSigno.Fc, 400m), (incidencia.Tipo, incidencia.Variable, incidencia.ValorRecibido));
        var lectura = Assert.Single(_e.Historial.Lecturas);
        Assert.Equal((null, 16), (lectura.Fc, lectura.Fr));
    }

    [Fact]
    public async Task Sin_ninguna_variable_valida_no_se_guarda_la_lectura_pero_si_la_incidencia()
    {
        await PacienteConSensorAsync();

        var resultado = await Registrar(_e.Lectura(new(Fc: 400)));

        Assert.Equal("Rechazada", resultado.Valor.Estado);
        Assert.Empty(_e.Historial.Lecturas);
        Assert.Single(_e.Historial.Incidencias);
        Assert.Empty(_e.Manejador.Eventos);
    }

    [Fact]
    public async Task Una_caida_sin_signos_validos_si_se_guarda()
    {
        await PacienteConSensorAsync();

        var resultado = await Registrar(_e.Lectura(new(), caida: true));

        Assert.Equal("Aceptada", resultado.Valor.Estado);
        Assert.True(Assert.Single(_e.Historial.Lecturas).Caida);
        Assert.NotNull(Assert.Single(_e.Historial.Estados).UltimaCaidaEn);
    }

    [Fact]
    public async Task La_presion_incompleta_descarta_el_par_registra_la_incidencia_y_pide_una_nueva_lectura()
    {
        await PacienteConSensorAsync();

        var resultado = await Registrar(_e.Lectura(new(Fc: 80, Pas: 118)));

        Assert.Equal("AceptadaParcial", resultado.Valor.Estado);
        var incidencia = Assert.Single(_e.Historial.Incidencias);
        Assert.Equal((TipoIncidencia.PresionIncompleta, true), (incidencia.Tipo, incidencia.RequiereNuevaLectura));
        var lectura = Assert.Single(_e.Historial.Lecturas);
        Assert.Equal((80, null, null), (lectura.Fc, lectura.Pas, lectura.Pad));
        var solicitud = Assert.Single(_e.SolicitudNuevaLectura.Solicitudes);
        Assert.Equal("ESP32-001", solicitud.CodigoDispositivo);
    }

    [Fact]
    public async Task Una_presion_incompleta_sin_otros_signos_se_rechaza_pero_igual_pide_otra_lectura()
    {
        await PacienteConSensorAsync();

        var resultado = await Registrar(_e.Lectura(new(Pad: 76)));

        Assert.Equal("Rechazada", resultado.Valor.Estado);
        Assert.Single(_e.SolicitudNuevaLectura.Solicitudes);
    }

    [Theory]
    [InlineData(121)]
    [InlineData(-24 * 3600 - 1)]
    public async Task Una_marca_de_tiempo_fuera_de_la_ventana_se_rechaza_con_incidencia(int segundos)
    {
        await PacienteConSensorAsync();

        var resultado = await Registrar(_e.Lectura(new(Fc: 80), segundos: segundos));

        Assert.Equal("Rechazada", resultado.Valor.Estado);
        Assert.Equal(TipoIncidencia.MarcaDeTiempoInvalida, Assert.Single(_e.Historial.Incidencias).Tipo);
        Assert.Empty(_e.Historial.Lecturas);
    }

    [Fact]
    public async Task Una_lectura_del_bufer_dentro_de_las_24_horas_se_acepta()
    {
        await PacienteConSensorAsync();

        var resultado = await Registrar(_e.Lectura(new(Fc: 80), segundos: -23 * 3600));

        Assert.Equal("Aceptada", resultado.Valor.Estado);
    }

    [Fact]
    public async Task Un_dispositivo_desconocido_o_sin_vincular_no_guarda_nada()
    {
        _e.AgregarDispositivo("ESP32-002");

        var desconocido = await Registrar(_e.Lectura(new(Fc: 80), dispositivo: "ESP32-999"));
        var sinVincular = await Registrar(_e.Lectura(new(Fc: 80), dispositivo: "ESP32-002"));

        Assert.Equal(("Rechazada", "Rechazada"), (desconocido.Valor.Estado, sinVincular.Valor.Estado));
        Assert.Equal([TipoIncidencia.DispositivoDesconocido, TipoIncidencia.DispositivoSinVincular], _e.Historial.Incidencias.Select(i => i.Tipo));
        Assert.All(_e.Historial.Incidencias, i => Assert.Null(i.HospitalizacionId));
        Assert.Empty(_e.Historial.Lecturas);
    }

    [Fact]
    public async Task Una_lectura_repetida_es_duplicada_sin_error_ni_incidencia()
    {
        await PacienteConSensorAsync();
        var lectura = _e.Lectura(new(Fc: 400, Fr: 16));
        await Registrar(lectura);

        var repetida = await Registrar(lectura);

        Assert.Equal("Duplicada", repetida.Valor.Estado);
        Assert.Single(_e.Historial.Lecturas);
        Assert.Single(_e.Historial.Incidencias);
    }

    [Fact]
    public async Task Si_la_base_detecta_el_duplicado_al_guardar_tambien_es_duplicada()
    {
        await PacienteConSensorAsync();
        _e.Historial.ErroresAlGuardar.Enqueue(ErroresTelemetria.LecturaDuplicada);

        var resultado = await Registrar(_e.Lectura(new(Fc: 80)));

        Assert.Equal("Duplicada", resultado.Valor.Estado);
        Assert.Empty(_e.Historial.Lecturas);
        Assert.Empty(_e.Manejador.Eventos);
    }

    [Fact]
    public async Task Un_conflicto_de_concurrencia_en_el_estado_se_reintenta()
    {
        await PacienteConSensorAsync();
        _e.Historial.ErroresAlGuardar.Enqueue(ErroresComunes.Concurrencia);

        var resultado = await Registrar(_e.Lectura(new(Fc: 80)));

        Assert.Equal("Aceptada", resultado.Valor.Estado);
        Assert.Equal(3, _e.Historial.Guardados); // intento con conflicto, reintento y la evaluación de riesgo
        Assert.Single(_e.Historial.Lecturas);
    }

    [Fact]
    public async Task Vincular_liberar_y_egresar_invalidan_la_resolucion_del_dispositivo()
    {
        var pacienteId = await PacienteConSensorAsync();
        await _e.LiberarDispositivo().EjecutarAsync(new(pacienteId, _e.EnfermeraId, null));
        await _e.VincularAsync(pacienteId, _e.Dispositivos.Dispositivos.Single());
        await _e.RegistrarEgreso().EjecutarAsync(new(pacienteId, "AltaMedica", null, _e.EnfermeraId, null));

        Assert.Equal(["ESP32-001", "ESP32-001", "ESP32-001", "ESP32-001"], _e.Resolutor.Invalidaciones);
    }

    [Fact]
    public async Task Despues_del_egreso_las_lecturas_ya_no_se_asocian_al_paciente()
    {
        var pacienteId = await PacienteConSensorAsync();
        await _e.RegistrarEgreso().EjecutarAsync(new(pacienteId, "AltaMedica", null, _e.EnfermeraId, null));

        var resultado = await Registrar(_e.Lectura(new(Fc: 80)));

        Assert.Equal("Rechazada", resultado.Valor.Estado);
        Assert.Equal(TipoIncidencia.DispositivoSinVincular, Assert.Single(_e.Historial.Incidencias).Tipo);
    }
}
