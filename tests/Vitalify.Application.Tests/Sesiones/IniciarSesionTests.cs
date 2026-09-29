using Vitalify.Application.Comun;
using Vitalify.Application.Sesiones;
using Vitalify.Application.Tests.Fakes;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Application.Tests.Sesiones;

public class IniciarSesionTests
{
    private readonly Escenario _e = new();

    private Task<Resultado<SesionIniciada>> Login(string correo, string contrasena) =>
        _e.IniciarSesion().EjecutarAsync(new IniciarSesionComando(correo, contrasena, "10.0.0.1"));

    [Fact]
    public async Task Con_credenciales_correctas_emite_tokens_y_devuelve_el_rol()
    {
        var medico = _e.AgregarUsuario("medico@hospital.pe", "Segura123", Rol.Medico);

        var resultado = await Login("  MEDICO@hospital.pe ", "Segura123");

        Assert.True(resultado.EsExito);
        Assert.Equal("Medico", resultado.Valor.Usuario.Rol);
        Assert.Equal(_e.Reloj.AhoraUtc.AddMinutes(15), resultado.Valor.ExpiraEn);
        Assert.False(string.IsNullOrEmpty(resultado.Valor.AccessToken));

        var sesion = Assert.Single(_e.Sesiones.Sesiones);
        Assert.Equal(medico.Id, sesion.UsuarioId);
        Assert.Equal("sha:" + resultado.Valor.RefreshToken, sesion.HashToken);
        Assert.Equal(_e.Reloj.AhoraUtc + Escenario.Opciones.DuracionMaxima, sesion.ExpiraEn);

        var registro = Assert.Single(_e.Auditoria.Registros);
        Assert.Equal(AccionAuditoria.LoginExitoso, registro.Accion);
        Assert.Equal(medico.Id, registro.UsuarioId);
    }

    [Fact]
    public async Task Con_contrasena_incorrecta_rechaza_sin_emitir_tokens_y_audita()
    {
        var enfermera = _e.AgregarUsuario("enfermera@hospital.pe", "Segura123");

        var resultado = await Login("enfermera@hospital.pe", "Incorrecta9");

        AssertCredencialesInvalidas(resultado);
        var registro = Assert.Single(_e.Auditoria.De(AccionAuditoria.LoginFallido));
        Assert.Equal(enfermera.Id, registro.UsuarioId);
        Assert.Contains("contraseña incorrecta", registro.Detalle);
        Assert.DoesNotContain("Incorrecta9", registro.Detalle);
    }

    [Fact]
    public async Task Un_correo_inexistente_da_el_mismo_error_y_tambien_verifica_un_hash()
    {
        _e.AgregarUsuario("enfermera@hospital.pe", "Segura123");

        var contrasenaMal = await Login("enfermera@hospital.pe", "Incorrecta9");
        var noExiste = await Login("nadie@hospital.pe", "Incorrecta9");

        AssertCredencialesInvalidas(noExiste);
        Assert.Equal(contrasenaMal.Error, noExiste.Error);
        Assert.Equal(1, _e.Hasher.VerificacionesSinUsuario);
        Assert.Equal(2, _e.Auditoria.De(AccionAuditoria.LoginFallido).Count());
    }

    [Fact]
    public async Task Un_usuario_inactivo_no_puede_iniciar_sesion()
    {
        var usuario = _e.AgregarUsuario("enfermera@hospital.pe", "Segura123");
        usuario.Desactivar(_e.Reloj.AhoraUtc);

        var resultado = await Login("enfermera@hospital.pe", "Segura123");

        AssertCredencialesInvalidas(resultado);
        Assert.Contains("usuario inactivo", Assert.Single(_e.Auditoria.Registros).Detalle);
    }

    [Theory]
    [InlineData("", "Segura123", "correo")]
    [InlineData("enfermera@hospital.pe", "", "contrasena")]
    public async Task Campos_vacios_devuelven_validacion(string correo, string contrasena, string campo)
    {
        var resultado = await Login(correo, contrasena);

        Assert.Equal(TipoError.Validacion, resultado.Error.Tipo);
        Assert.Contains(campo, resultado.Error.Detalles.Keys);
    }

    private void AssertCredencialesInvalidas(Resultado<SesionIniciada> resultado)
    {
        Assert.False(resultado.EsExito);
        Assert.Equal(TipoError.NoAutorizado, resultado.Error.Tipo);
        Assert.Equal("credenciales-invalidas", resultado.Error.Codigo);
        Assert.Equal("Correo o contraseña incorrectos.", resultado.Error.Mensaje);
        Assert.Empty(_e.Sesiones.Sesiones);
    }
}
