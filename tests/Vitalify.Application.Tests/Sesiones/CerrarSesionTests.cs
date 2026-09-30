using Vitalify.Application.Sesiones;
using Vitalify.Application.Tests.Fakes;
using Vitalify.Domain.Auditoria;

namespace Vitalify.Application.Tests.Sesiones;

public class CerrarSesionTests
{
    private readonly Escenario _e = new();

    [Fact]
    public async Task Revoca_el_refresh_token_y_guarda_el_jti_hasta_que_expire()
    {
        _e.AgregarUsuario("medico@hospital.pe", "Segura123");
        var login = await _e.IniciarSesionAsync("medico@hospital.pe", "Segura123");
        var expira = _e.Reloj.AhoraUtc.AddMinutes(15);

        var resultado = await _e.CerrarSesion().EjecutarAsync(
            new CerrarSesionComando(login.Usuario.Id, "jti-1", expira, login.RefreshToken, "10.0.0.1"));

        Assert.True(resultado.EsExito);
        Assert.True(Assert.Single(_e.Sesiones.Sesiones).EstaRevocada);
        var token = Assert.Single(_e.TokensRevocados.Tokens);
        Assert.Equal(("jti-1", login.Usuario.Id, expira), (token.Jti, token.UsuarioId, token.ExpiraEn));
        Assert.Equal(login.Usuario.Id, Assert.Single(_e.Auditoria.De(AccionAuditoria.Logout)).UsuarioId);
    }

    [Fact]
    public async Task No_revoca_la_sesion_de_otro_usuario()
    {
        _e.AgregarUsuario("medico@hospital.pe", "Segura123");
        var ajena = await _e.IniciarSesionAsync("medico@hospital.pe", "Segura123");

        await _e.CerrarSesion().EjecutarAsync(
            new CerrarSesionComando(Guid.NewGuid(), "jti-2", _e.Reloj.AhoraUtc.AddMinutes(15), ajena.RefreshToken, null));

        Assert.False(Assert.Single(_e.Sesiones.Sesiones).EstaRevocada);
    }

    [Fact]
    public async Task Sin_refresh_token_igual_invalida_el_access_token()
    {
        var usuarioId = Guid.NewGuid();

        var resultado = await _e.CerrarSesion().EjecutarAsync(
            new CerrarSesionComando(usuarioId, "jti-3", _e.Reloj.AhoraUtc.AddMinutes(15), null, null));

        Assert.True(resultado.EsExito);
        Assert.Equal("jti-3", Assert.Single(_e.TokensRevocados.Tokens).Jti);
    }
}
