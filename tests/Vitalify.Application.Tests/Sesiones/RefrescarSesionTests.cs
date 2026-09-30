using Vitalify.Application.Comun;
using Vitalify.Application.Sesiones;
using Vitalify.Application.Tests.Fakes;
using Vitalify.Domain.Auditoria;

namespace Vitalify.Application.Tests.Sesiones;

public class RefrescarSesionTests
{
    private readonly Escenario _e = new();

    public RefrescarSesionTests() => _e.AgregarUsuario("enfermera@hospital.pe", "Segura123");

    private Task<Resultado<SesionIniciada>> Refrescar(string refreshToken) =>
        _e.RefrescarSesion().EjecutarAsync(new RefrescarSesionComando(refreshToken, "10.0.0.1"));

    [Fact]
    public async Task Rota_el_refresh_token_y_emite_un_nuevo_access_token()
    {
        var login = await _e.IniciarSesionAsync("enfermera@hospital.pe", "Segura123");
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(14));

        var resultado = await Refrescar(login.RefreshToken);

        Assert.True(resultado.EsExito);
        Assert.NotEqual(login.RefreshToken, resultado.Valor.RefreshToken);
        Assert.NotEqual(login.AccessToken, resultado.Valor.AccessToken);

        var anterior = _e.Sesiones.Sesiones.Single(s => s.HashToken == "sha:" + login.RefreshToken);
        var nueva = _e.Sesiones.Sesiones.Single(s => s.HashToken == "sha:" + resultado.Valor.RefreshToken);
        Assert.True(anterior.EstaRevocada);
        Assert.Equal(nueva.Id, anterior.ReemplazadaPorId);
        Assert.Equal(anterior.ExpiraEn, nueva.ExpiraEn);
    }

    [Fact]
    public async Task Tras_el_tiempo_de_inactividad_la_sesion_expira_y_se_audita()
    {
        var login = await _e.IniciarSesionAsync("enfermera@hospital.pe", "Segura123");
        _e.Reloj.Avanzar(Escenario.Opciones.InactividadMaxima);

        var resultado = await Refrescar(login.RefreshToken);

        Assert.Equal(TipoError.NoAutorizado, resultado.Error.Tipo);
        Assert.Equal("sesion-expirada", resultado.Error.Codigo);
        Assert.Contains("inactividad", Assert.Single(_e.Auditoria.De(AccionAuditoria.SesionExpirada)).Detalle);
    }

    [Fact]
    public async Task Justo_antes_del_limite_de_inactividad_todavia_se_puede_refrescar()
    {
        var login = await _e.IniciarSesionAsync("enfermera@hospital.pe", "Segura123");
        _e.Reloj.Avanzar(Escenario.Opciones.InactividadMaxima - TimeSpan.FromSeconds(1));

        Assert.True((await Refrescar(login.RefreshToken)).EsExito);
    }

    [Fact]
    public async Task La_sesion_expira_al_cumplir_la_duracion_maxima_aunque_se_refresque_seguido()
    {
        var token = (await _e.IniciarSesionAsync("enfermera@hospital.pe", "Segura123")).RefreshToken;
        var fin = _e.Reloj.AhoraUtc + Escenario.Opciones.DuracionMaxima;

        while (_e.Reloj.AhoraUtc + TimeSpan.FromMinutes(25) < fin)
        {
            _e.Reloj.Avanzar(TimeSpan.FromMinutes(25));
            var refresco = await Refrescar(token);
            Assert.True(refresco.EsExito);
            token = refresco.Valor.RefreshToken;
        }

        _e.Reloj.AhoraUtc = fin;
        var resultado = await Refrescar(token);

        Assert.Equal("sesion-expirada", resultado.Error.Codigo);
        Assert.Contains("duración máxima", Assert.Single(_e.Auditoria.De(AccionAuditoria.SesionExpirada)).Detalle);
    }

    [Fact]
    public async Task Reusar_un_token_ya_rotado_revoca_todas_las_sesiones_del_usuario()
    {
        var dispositivoA = await _e.IniciarSesionAsync("enfermera@hospital.pe", "Segura123");
        var dispositivoB = await _e.IniciarSesionAsync("enfermera@hospital.pe", "Segura123");
        var rotado = await Refrescar(dispositivoA.RefreshToken);

        var reuso = await Refrescar(dispositivoA.RefreshToken);

        Assert.Equal("token-invalido", reuso.Error.Codigo);
        Assert.All(_e.Sesiones.Sesiones, s => Assert.True(s.EstaRevocada));
        Assert.False((await Refrescar(rotado.Valor.RefreshToken)).EsExito);
        Assert.False((await Refrescar(dispositivoB.RefreshToken)).EsExito);
        Assert.Contains("Reuso", Assert.Single(_e.Auditoria.De(AccionAuditoria.AccesoDenegado)).Detalle);
    }

    [Fact]
    public async Task Un_token_desconocido_es_invalido()
    {
        var resultado = await Refrescar("no-existe");

        Assert.Equal("token-invalido", resultado.Error.Codigo);
    }

    [Fact]
    public async Task El_token_de_una_sesion_cerrada_es_invalido()
    {
        var login = await _e.IniciarSesionAsync("enfermera@hospital.pe", "Segura123");
        var usuarioId = login.Usuario.Id;
        await _e.CerrarSesion().EjecutarAsync(new CerrarSesionComando(usuarioId, "jti-x", _e.Reloj.AhoraUtc.AddMinutes(15), login.RefreshToken, null));

        var resultado = await Refrescar(login.RefreshToken);

        Assert.Equal("token-invalido", resultado.Error.Codigo);
    }

    [Fact]
    public async Task Si_el_usuario_fue_desactivado_no_puede_refrescar()
    {
        var login = await _e.IniciarSesionAsync("enfermera@hospital.pe", "Segura123");
        _e.Usuarios.Usuarios.Single().Desactivar(_e.Reloj.AhoraUtc);

        var resultado = await Refrescar(login.RefreshToken);

        Assert.Equal("token-invalido", resultado.Error.Codigo);
    }
}
