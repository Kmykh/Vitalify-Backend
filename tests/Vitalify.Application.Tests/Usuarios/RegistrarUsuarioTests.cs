using Vitalify.Application.Comun;
using Vitalify.Application.Tests.Fakes;
using Vitalify.Application.Usuarios;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Application.Tests.Usuarios;

public class RegistrarUsuarioTests
{
    private readonly Escenario _e = new();
    private readonly Guid _adminId = Guid.NewGuid();

    private RegistrarUsuarioComando Comando(
        string correo = "Enfermera@Hospital.pe", string contrasena = "Segura123", string rol = "Enfermera", string nombre = "Lucía Ramos") =>
        new(nombre, correo, contrasena, rol, _adminId, "10.0.0.5");

    [Fact]
    public async Task Registra_al_usuario_con_hash_correo_normalizado_y_auditoria()
    {
        var resultado = await _e.RegistrarUsuario().EjecutarAsync(Comando());

        Assert.True(resultado.EsExito);
        var usuario = Assert.Single(_e.Usuarios.Usuarios);
        Assert.Equal("enfermera@hospital.pe", usuario.Correo.Valor);
        Assert.Equal("hash:Segura123", usuario.HashContrasena);
        Assert.Equal(Rol.Enfermera, usuario.Rol);
        Assert.Equal(usuario.Id, resultado.Valor.Id);
        Assert.Equal("Enfermera", resultado.Valor.Rol);

        var registro = Assert.Single(_e.Auditoria.De(AccionAuditoria.UsuarioCreado));
        Assert.Equal(_adminId, registro.UsuarioId);
        Assert.Equal("10.0.0.5", registro.Ip);
        Assert.Equal(1, _e.UnidadDeTrabajo.Guardados);
    }

    [Theory]
    [InlineData("medico")]
    [InlineData("MEDICO")]
    [InlineData("Enfermera")]
    public void Acepta_los_roles_registrables_sin_distinguir_mayusculas(string rol)
    {
        Assert.True(RegistrarUsuarioValidador.TryParseRol(rol, out _));
    }

    [Fact]
    public async Task Correo_duplicado_devuelve_conflicto_aunque_cambien_las_mayusculas()
    {
        _e.AgregarUsuario("enfermera@hospital.pe", "Segura123");

        var resultado = await _e.RegistrarUsuario().EjecutarAsync(Comando(correo: " ENFERMERA@hospital.pe"));

        Assert.False(resultado.EsExito);
        Assert.Equal(TipoError.Conflicto, resultado.Error.Tipo);
        Assert.Equal("correo-en-uso", resultado.Error.Codigo);
        Assert.Single(_e.Usuarios.Usuarios);
        Assert.Empty(_e.Auditoria.Registros);
    }

    [Fact]
    public async Task Una_violacion_de_unicidad_al_guardar_tambien_es_conflicto()
    {
        _e.UnidadDeTrabajo.ErrorAlGuardar = ErroresUsuario.CorreoEnUso;

        var resultado = await _e.RegistrarUsuario().EjecutarAsync(Comando());

        Assert.False(resultado.EsExito);
        Assert.Equal(TipoError.Conflicto, resultado.Error.Tipo);
    }

    [Theory]
    [InlineData("Administrador")]
    [InlineData("1")]
    [InlineData("Director")]
    [InlineData("")]
    public async Task Solo_permite_los_roles_Medico_o_Enfermera(string rol)
    {
        var resultado = await _e.RegistrarUsuario().EjecutarAsync(Comando(rol: rol));

        AssertValidacion(resultado, "rol");
    }

    [Theory]
    [InlineData("corta1")]
    [InlineData("solotexto")]
    [InlineData("12345678")]
    [InlineData("")]
    public async Task La_contrasena_necesita_8_caracteres_con_letra_y_numero(string contrasena)
    {
        var resultado = await _e.RegistrarUsuario().EjecutarAsync(Comando(contrasena: contrasena));

        AssertValidacion(resultado, "contrasena");
    }

    [Theory]
    [InlineData("no-es-correo")]
    [InlineData("")]
    public async Task El_correo_debe_ser_valido(string correo)
    {
        var resultado = await _e.RegistrarUsuario().EjecutarAsync(Comando(correo: correo));

        AssertValidacion(resultado, "correo");
    }

    [Fact]
    public async Task El_nombre_es_obligatorio()
    {
        var resultado = await _e.RegistrarUsuario().EjecutarAsync(Comando(nombre: "  "));

        AssertValidacion(resultado, "nombre");
    }

    private void AssertValidacion<T>(Resultado<T> resultado, string campo)
    {
        Assert.False(resultado.EsExito);
        Assert.Equal(TipoError.Validacion, resultado.Error.Tipo);
        Assert.Contains(campo, resultado.Error.Detalles.Keys);
        Assert.Empty(_e.Usuarios.Usuarios);
        Assert.Equal(0, _e.UnidadDeTrabajo.Guardados);
    }
}
