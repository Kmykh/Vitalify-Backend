using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Vitalify.Api.IntegrationTests.Infraestructura;
using Vitalify.Application.Sesiones;
using Vitalify.Domain.Auditoria;

namespace Vitalify.Api.IntegrationTests;

/// <summary>HU04 · Cierre de sesión seguro.</summary>
public class HU04CierreDeSesionTests(FabricaVitalify fabrica) : PruebaApi(fabrica)
{
    [Fact]
    public async Task HU04_E1_ElMismoTokenUsadoDespuesDelLogout_Devuelve401()
    {
        var (cliente, sesion) = await ClienteConRolAsync("Enfermera");
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/api/v1/auth/yo")).StatusCode);

        var logout = await cliente.PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = sesion.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var reuso = await cliente.GetAsync("/api/v1/auth/yo");
        Assert.Equal(HttpStatusCode.Unauthorized, reuso.StatusCode);
        Assert.Equal("sesion-cerrada", (await reuso.LeerProblemaAsync()).Type);

        // El refresh token de esa sesión tampoco sirve.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Fabrica.CrearCliente().RefrescarAsync(sesion.RefreshToken)).StatusCode);

        Assert.True(await Fabrica.ConsultarAsync(db => db.TokensRevocados.AnyAsync(t => t.UsuarioId == sesion.Usuario.Id)));
        Assert.True(await Fabrica.ConsultarAsync(db => db.Auditoria.AnyAsync(a =>
            a.Accion == AccionAuditoria.Logout && a.UsuarioId == sesion.Usuario.Id)));
    }

    [Fact]
    public async Task HU04_E2_SinInteraccionDuranteElTiempoConfigurado_LaSesionExpiraYPideLogin()
    {
        var (_, sesion) = await ClienteConRolAsync("Medico");

        HttpResponseMessage respuesta;
        using (Fabrica.Reloj.Adelantar(TimeSpan.FromMinutes(31)))
        {
            respuesta = await Fabrica.CrearCliente().RefrescarAsync(sesion.RefreshToken);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        var problema = await respuesta.LeerProblemaAsync();
        Assert.Equal("sesion-expirada", problema.Type);
        Assert.True(await Fabrica.ConsultarAsync(db => db.Auditoria.AnyAsync(a =>
            a.Accion == AccionAuditoria.SesionExpirada && a.UsuarioId == sesion.Usuario.Id)));
    }

    [Fact]
    public async Task ElRefreshRotaElRefreshToken()
    {
        var (_, sesion) = await ClienteConRolAsync("Enfermera");
        var cliente = Fabrica.CrearCliente();

        var respuesta = await cliente.RefrescarAsync(sesion.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var nueva = (await respuesta.Content.ReadFromJsonAsync<SesionIniciada>())!;
        Assert.NotEqual(sesion.RefreshToken, nueva.RefreshToken);
        Assert.NotEqual(sesion.AccessToken, nueva.AccessToken);
        Assert.Equal(sesion.Usuario, nueva.Usuario);

        cliente.UsarToken(nueva.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/api/v1/auth/yo")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cliente.RefrescarAsync(nueva.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task ReusarUnRefreshTokenYaRotado_RevocaTodasLasSesionesDelUsuario()
    {
        var administrador = await ClienteAdministradorAsync();
        var correo = await administrador.RegistrarUsuarioAsync("Enfermera");
        var dispositivoA = await Fabrica.CrearCliente().IniciarSesionAsync(correo, ClienteApi.ContrasenaDePrueba);
        var dispositivoB = await Fabrica.CrearCliente().IniciarSesionAsync(correo, ClienteApi.ContrasenaDePrueba);
        var cliente = Fabrica.CrearCliente();
        var rotada = (await (await cliente.RefrescarAsync(dispositivoA.RefreshToken)).Content.ReadFromJsonAsync<SesionIniciada>())!;

        var reuso = await cliente.RefrescarAsync(dispositivoA.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, reuso.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.RefrescarAsync(rotada.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.RefrescarAsync(dispositivoB.RefreshToken)).StatusCode);
        Assert.False(await Fabrica.ConsultarAsync(db => db.SesionesRefresco.AnyAsync(s =>
            s.UsuarioId == dispositivoA.Usuario.Id && s.RevocadaEn == null)));
    }

    [Fact]
    public async Task LaSesionExpiraAlCumplirLaDuracionMaximaAunqueSeRefresqueSeguido()
    {
        var (_, sesion) = await ClienteConRolAsync("Enfermera");
        var cliente = Fabrica.CrearCliente();
        var refreshToken = sesion.RefreshToken;

        HttpResponseMessage respuesta;
        using (var _ = Fabrica.Reloj.Adelantar(TimeSpan.Zero))
        {
            // 28 refrescos cada 25 minutos (11 h 40 min) funcionan; el turno de 12 h vence en el siguiente.
            for (var i = 0; i < 28; i++)
            {
                Fabrica.Reloj.Adelantar(TimeSpan.FromMinutes(25));
                var refresco = await cliente.RefrescarAsync(refreshToken);
                Assert.Equal(HttpStatusCode.OK, refresco.StatusCode);
                refreshToken = (await refresco.Content.ReadFromJsonAsync<SesionIniciada>())!.RefreshToken;
            }

            Fabrica.Reloj.Adelantar(TimeSpan.FromMinutes(25));
            respuesta = await cliente.RefrescarAsync(refreshToken);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Equal("sesion-expirada", (await respuesta.LeerProblemaAsync()).Type);
        Assert.True(await Fabrica.ConsultarAsync(db => db.Auditoria.AnyAsync(a =>
            a.Accion == AccionAuditoria.SesionExpirada && a.UsuarioId == sesion.Usuario.Id && a.Detalle!.Contains("duración máxima"))));
    }
}
