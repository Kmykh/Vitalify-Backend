using Vitalify.Application.Comun;
using Vitalify.Application.Pacientes;
using Vitalify.Application.Tests.Fakes;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Hospitalizaciones;

namespace Vitalify.Application.Tests.Pacientes;

public class RegistrarIngresoPacienteTests
{
    private readonly Escenario _e = new();

    [Fact]
    public async Task Registra_al_paciente_abre_la_hospitalizacion_y_audita_sin_datos_personales()
    {
        var cama = _e.AgregarCama();

        var resultado = await _e.RegistrarIngresoPaciente().EjecutarAsync(_e.ComandoIngreso(cama.Id));

        Assert.True(resultado.EsExito);
        var ficha = resultado.Valor;
        Assert.Equal("Rosa Huamán Quispe", ficha.NombreCompleto);
        Assert.Equal(68, ficha.Edad);
        Assert.Equal("MED-B-01", ficha.HospitalizacionActiva!.Cama.Codigo);
        Assert.Null(ficha.HospitalizacionActiva.Dispositivo);

        var hospitalizacion = Assert.Single(_e.Hospitalizaciones.Hospitalizaciones);
        Assert.True(hospitalizacion.EstaActiva);
        Assert.Equal(_e.EnfermeraId, hospitalizacion.RegistradoPor);

        var registro = Assert.Single(_e.Auditoria.De(AccionAuditoria.PacienteIngresado));
        Assert.Equal(_e.EnfermeraId, registro.UsuarioId);
        Assert.Contains(ficha.Id.ToString(), registro.Detalle);
        Assert.DoesNotContain("Rosa", registro.Detalle);
        Assert.DoesNotContain("12345678", registro.Detalle);
    }

    [Fact]
    public async Task Con_solo_la_edad_estima_el_1_de_enero_y_lo_marca_como_estimado()
    {
        var cama = _e.AgregarCama();

        var ficha = (await _e.RegistrarIngresoPaciente().EjecutarAsync(_e.ComandoIngreso(cama.Id, fechaNacimiento: null, edad: 70))).Valor;

        Assert.Equal(new DateOnly(_e.Reloj.AhoraUtc.Year - 70, 1, 1), ficha.FechaNacimiento);
        Assert.True(ficha.FechaNacimientoEstimada);
        Assert.Equal(70, ficha.Edad);
    }

    [Fact]
    public async Task Los_campos_obligatorios_vacios_devuelven_un_error_por_campo()
    {
        var comando = new RegistrarIngresoPacienteComando("", "", "", null, null, null, "", _e.EnfermeraId, null);

        var resultado = await _e.RegistrarIngresoPaciente().EjecutarAsync(comando);

        Assert.Equal(TipoError.Validacion, resultado.Error.Tipo);
        Assert.Equal(
            ["camaId", "diagnosticoIngreso", "fechaNacimiento", "nombreCompleto", "numeroDocumento", "tipoDocumento"],
            resultado.Error.Detalles.Keys.Order());
        Assert.All(resultado.Error.Detalles.Values, mensajes => Assert.Single(mensajes));
        Assert.Empty(_e.Pacientes.Pacientes);
    }

    [Theory]
    [InlineData("1234567", null, null, "numeroDocumento", "El DNI debe tener 8 dígitos.")]
    [InlineData("12345678", "2090-01-01", null, "fechaNacimiento", "La fecha de nacimiento no puede ser futura.")]
    [InlineData("12345678", "1890-01-01", null, "fechaNacimiento", "La edad resultante debe estar entre 0 y 120 años.")]
    [InlineData("12345678", "14/03/1958", null, "fechaNacimiento", "La fecha de nacimiento debe tener el formato AAAA-MM-DD.")]
    [InlineData("12345678", "1958-03-14", 68, "fechaNacimiento", "Envía la fecha de nacimiento o la edad, no ambas.")]
    [InlineData("12345678", null, 121, "edad", "La edad debe estar entre 0 y 120 años.")]
    public async Task Valida_documento_fecha_y_edad(string documento, string? fecha, int? edad, string campo, string mensaje)
    {
        var cama = _e.AgregarCama();

        var resultado = await _e.RegistrarIngresoPaciente().EjecutarAsync(
            _e.ComandoIngreso(cama.Id, documento, fechaNacimiento: fecha, edad: edad) with { FechaNacimiento = fecha });

        Assert.Equal(TipoError.Validacion, resultado.Error.Tipo);
        Assert.Equal([mensaje], resultado.Error.Detalles[campo]);
    }

    [Fact]
    public async Task Una_cama_ocupada_devuelve_conflicto()
    {
        await _e.IngresarAsync("MED-B-01", "11111111");
        var cama = _e.Camas.Camas.Single();

        var resultado = await _e.RegistrarIngresoPaciente().EjecutarAsync(_e.ComandoIngreso(cama.Id, "22222222"));

        Assert.Equal("cama-ocupada", resultado.Error.Codigo);
    }

    [Fact]
    public async Task Una_cama_inexistente_o_inactiva_es_un_error_de_validacion_de_camaId()
    {
        var inexistente = await _e.RegistrarIngresoPaciente().EjecutarAsync(_e.ComandoIngreso(Guid.NewGuid()));
        var cama = _e.AgregarCama();
        cama.Desactivar();
        var inactiva = await _e.RegistrarIngresoPaciente().EjecutarAsync(_e.ComandoIngreso(cama.Id));

        Assert.Equal(["La cama no existe."], inexistente.Error.Detalles["camaId"]);
        Assert.Equal(["La cama no está activa."], inactiva.Error.Detalles["camaId"]);
    }

    [Fact]
    public async Task Un_paciente_con_hospitalizacion_activa_no_puede_ingresar_de_nuevo()
    {
        await _e.IngresarAsync("MED-B-01", "11111111");
        var otraCama = _e.AgregarCama("MED-B-02");

        var resultado = await _e.RegistrarIngresoPaciente().EjecutarAsync(_e.ComandoIngreso(otraCama.Id, "11111111"));

        Assert.Equal("paciente-ya-hospitalizado", resultado.Error.Codigo);
        Assert.Single(_e.Hospitalizaciones.Hospitalizaciones);
    }

    [Fact]
    public async Task El_reingreso_despues_del_egreso_reutiliza_al_mismo_paciente_sin_cambiar_sus_datos()
    {
        var primera = await _e.IngresarAsync("MED-B-01", "11111111");
        await _e.RegistrarEgreso().EjecutarAsync(new RegistrarEgresoComando(primera.Id, "AltaMedica", null, _e.EnfermeraId, null));
        var cama = _e.AgregarCama("MED-B-02");

        var reingreso = await _e.RegistrarIngresoPaciente().EjecutarAsync(
            _e.ComandoIngreso(cama.Id, "11111111", nombre: "Nombre distinto"));

        Assert.Equal(primera.Id, reingreso.Valor.Id);
        Assert.Equal("Rosa Huamán Quispe", reingreso.Valor.NombreCompleto);
        Assert.Single(_e.Pacientes.Pacientes);
        Assert.Equal(2, _e.Hospitalizaciones.Hospitalizaciones.Count);
        Assert.Single(_e.Hospitalizaciones.Hospitalizaciones, h => h.Estado == EstadoHospitalizacion.Activa);
    }

    [Fact]
    public async Task Actualizar_datos_corrige_nombre_fecha_y_diagnostico()
    {
        var ficha = await _e.IngresarAsync();

        var resultado = await _e.ActualizarDatosPaciente().EjecutarAsync(new ActualizarDatosPacienteComando(
            ficha.Id, "Rosa María Huamán Quispe", null, 67, "Neumonía grave", _e.EnfermeraId, null));

        Assert.True(resultado.EsExito);
        Assert.Equal("Rosa María Huamán Quispe", resultado.Valor.NombreCompleto);
        Assert.True(resultado.Valor.FechaNacimientoEstimada);
        Assert.Equal("Neumonía grave", resultado.Valor.HospitalizacionActiva!.DiagnosticoIngreso);
        Assert.Single(_e.Auditoria.De(AccionAuditoria.PacienteActualizado));
    }

    [Fact]
    public async Task Actualizar_un_paciente_sin_hospitalizacion_activa_devuelve_no_encontrado()
    {
        var ficha = await _e.IngresarAsync();
        await _e.RegistrarEgreso().EjecutarAsync(new RegistrarEgresoComando(ficha.Id, "Traslado", null, _e.EnfermeraId, null));

        var resultado = await _e.ActualizarDatosPaciente().EjecutarAsync(new ActualizarDatosPacienteComando(
            ficha.Id, "Rosa Huamán", "1958-03-14", null, "Neumonía", _e.EnfermeraId, null));

        Assert.Equal(TipoError.NoEncontrado, resultado.Error.Tipo);
        Assert.Equal("sin-hospitalizacion-activa", resultado.Error.Codigo);
    }

    [Fact]
    public async Task La_ficha_queda_auditada_sin_datos_personales()
    {
        var ficha = await _e.IngresarAsync();

        var resultado = await _e.ObtenerPaciente().EjecutarAsync(new ObtenerPacienteConsulta(ficha.Id, _e.EnfermeraId, "10.0.0.9"));

        Assert.Equal(ficha.Id, resultado.Valor.Id);
        var registro = Assert.Single(_e.Auditoria.De(AccionAuditoria.ConsultaFichaPaciente));
        Assert.Equal(("10.0.0.9", $"Paciente {ficha.Id}."), (registro.Ip, registro.Detalle));
    }

    [Fact]
    public async Task Listar_hospitalizados_busca_por_nombre_cama_o_documento()
    {
        await _e.IngresarAsync("MED-B-01", "11111111");
        await _e.IngresarAsync("MED-B-02", "22222222");

        var porCama = await _e.ListarPacientesHospitalizados().EjecutarAsync(new ListarPacientesHospitalizadosConsulta(Buscar: "b-02"));
        var porDocumento = await _e.ListarPacientesHospitalizados().EjecutarAsync(new ListarPacientesHospitalizadosConsulta(Buscar: "11111111"));
        var todos = await _e.ListarPacientesHospitalizados().EjecutarAsync(new ListarPacientesHospitalizadosConsulta());

        Assert.Equal("MED-B-02", Assert.Single(porCama.Valor.Elementos).Cama);
        Assert.Equal("MED-B-01", Assert.Single(porDocumento.Valor.Elementos).Cama);
        Assert.Equal(2, todos.Valor.Total);
    }
}
