using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Vitalify.Api.IntegrationTests.Infraestructura;
using Vitalify.Application.Sesiones;
using Vitalify.Domain.Auditoria;

namespace Vitalify.Api.IntegrationTests;

/// <summary>HU02 · Inicio de sesión seguro con JWT.</summary>
public class HU02InicioDeSesionTests(FabricaVitalify fabrica) : PruebaApi(fabrica)
{
    [Fact]
    public async Task HU02_E1_CredencialesCorrectas_DevuelveJwtYElRolParaRedirigir()
    {
        var correo = await (await ClienteAdministradorAsync()).RegistrarUsuarioAsync("Enfermera");

        var respuesta = await Fabrica.CrearCliente().LoginAsync(correo, ClienteApi.ContrasenaDePrueba);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var sesion = (await respuesta.Content.ReadFromJsonAsync<SesionIniciada>())!;
        Assert.Equal("Enfermera", sesion.Usuario.Rol);
        Assert.Equal(correo, sesion.Usuario.Correo);
        Assert.False(string.IsNullOrWhiteSpace(sesion.RefreshToken));
        Assert.InRange(sesion.ExpiraEn - DateTime.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(sesion.AccessToken);
        Assert.Equal("HS256", jwt.Alg);
        Assert.Equal(sesion.Usuario.Id.ToString(), jwt.Subject);
        Assert.Equal(correo, jwt.GetClaim("email").Value);
        Assert.Equal("Enfermera de prueba", jwt.GetClaim("name").Value);
        Assert.Equal("Enfermera", jwt.GetClaim("role").Value);
        Assert.False(string.IsNullOrEmpty(jwt.Id));

        Assert.True(await Fabrica.ConsultarAsync(db => db.Auditoria.AnyAsync(a =>
            a.Accion == AccionAuditoria.LoginExitoso && a.UsuarioId == sesion.Usuario.Id)));

        // El refresh token no se guarda en claro.
        Assert.False(await Fabrica.ConsultarAsync(db => db.SesionesRefresco.AnyAsync(s => s.HashToken == sesion.RefreshToken)));
    }

    [Fact]
    public async Task HU02_E2_ContrasenaIncorrecta_Devuelve401ConMensajeYSinToken()
    {
        var correo = await (await ClienteAdministradorAsync()).RegistrarUsuarioAsync("Medico");

        var respuesta = await Fabrica.CrearCliente().LoginAsync(correo, "Incorrecta99");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        Assert.DoesNotContain("accessToken", cuerpo);
        Assert.DoesNotContain("refreshToken", cuerpo);
        var problema = await respuesta.LeerProblemaAsync();
        Assert.Equal("credenciales-invalidas", problema.Type);
        Assert.Equal("Correo o contraseña incorrectos.", problema.Detail);

        var fallido = await Fabrica.ConsultarAsync(db => db.Auditoria
            .Where(a => a.Accion == AccionAuditoria.LoginFallido && a.Detalle!.Contains(correo))
            .SingleAsync());
        Assert.DoesNotContain("Incorrecta99", fallido.Detalle);
    }

    [Fact]
    public async Task UnCorreoQueNoExiste_DevuelveElMismoErrorQueUnaContrasenaIncorrecta()
    {
        var correo = await (await ClienteAdministradorAsync()).RegistrarUsuarioAsync("Medico");
        var cliente = Fabrica.CrearCliente();

        var contrasenaMal = await (await cliente.LoginAsync(correo, "Incorrecta99")).LeerProblemaAsync();
        var noExiste = await (await cliente.LoginAsync($"nadie.{Guid.NewGuid():N}@vitalify.test", "Incorrecta99")).LeerProblemaAsync();

        Assert.Equal((contrasenaMal.Status, contrasenaMal.Type, contrasenaMal.Detail), (noExiste.Status, noExiste.Type, noExiste.Detail));
    }

    [Fact]
    public async Task ElLoginSuperaElLimiteDeIntentosPorIp_Devuelve429()
    {
        var cliente = Fabrica.CrearCliente();

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.LoginAsync("nadie@vitalify.test", "Incorrecta99")).StatusCode);
        }

        var respuesta = await cliente.LoginAsync("nadie@vitalify.test", "Incorrecta99");

        Assert.Equal(HttpStatusCode.TooManyRequests, respuesta.StatusCode);
        Assert.Equal("demasiados-intentos", (await respuesta.LeerProblemaAsync()).Type);

        // Otra IP no está afectada.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Fabrica.CrearCliente().LoginAsync("nadie@vitalify.test", "Incorrecta99")).StatusCode);
    }
}
