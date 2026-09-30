using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Vitalify.Api.Contratos;
using Vitalify.Api.IntegrationTests.Infraestructura;
using Vitalify.Domain.Auditoria;

namespace Vitalify.Api.IntegrationTests;

/// <summary>HU03 · Control de acceso basado en roles (RBAC).</summary>
public class HU03ControlDeAccesoTests(FabricaVitalify fabrica) : PruebaApi(fabrica)
{
    [Fact]
    public async Task HU03_E1_EnfermeraEnEndpointExclusivoDeAdministracion_Devuelve403()
    {
        var (enfermera, sesion) = await ClienteConRolAsync("Enfermera");

        var respuesta = await enfermera.GetAsync("/api/v1/usuarios");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        var problema = await respuesta.LeerProblemaAsync();
        Assert.Equal("acceso-denegado", problema.Type);
        Assert.Equal("Acceso denegado", problema.Title);

        var registro = await Fabrica.ConsultarAsync(db => db.Auditoria
            .Where(a => a.Accion == AccionAuditoria.AccesoDenegado && a.UsuarioId == sesion.Usuario.Id)
            .SingleAsync());
        Assert.Contains("GET /api/v1/usuarios", registro.Detalle);
        Assert.False(string.IsNullOrEmpty(registro.Ip));
    }

    [Fact]
    public async Task HU03_E2_MedicoConJwtValidoEnUnRecursoDeSuRol_AccedeCon200()
    {
        var (medico, _) = await ClienteConRolAsync("Medico");

        var respuesta = await medico.GetAsync("/api/v1/pacientes/monitoreados");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var cuerpo = (await respuesta.Content.ReadFromJsonAsync<PacientesMonitoreadosRespuesta>())!;
        Assert.NotNull(cuerpo.Items);
    }

    [Fact]
    public async Task ElAdministradorNoAccedeAlMonitoreoClinico()
    {
        var respuesta = await (await ClienteAdministradorAsync()).GetAsync("/api/v1/pacientes/monitoreados");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task SinToken_UnEndpointProtegidoDevuelve401EnEspanol()
    {
        var respuesta = await Fabrica.CrearCliente().GetAsync("/api/v1/auth/yo");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Equal("Bearer", respuesta.Headers.WwwAuthenticate.Single().Scheme);
        var problema = await respuesta.LeerProblemaAsync();
        Assert.Equal("no-autenticado", problema.Type);
        Assert.Equal("No autenticado", problema.Title);
    }

    [Fact]
    public async Task UnTokenConLaFirmaAlterada_Devuelve401()
    {
        var (enfermera, sesion) = await ClienteConRolAsync("Enfermera");

        // Se cambia el rol a Administrador en el payload conservando la firma original.
        var partes = sesion.AccessToken.Split('.');
        var payload = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(partes[1])).Replace("\"Enfermera\"", "\"Administrador\"");
        partes[1] = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(payload));
        enfermera.UsarToken(string.Join('.', partes));

        var respuesta = await enfermera.GetAsync("/api/v1/usuarios");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Equal("token-invalido", (await respuesta.LeerProblemaAsync()).Type);
    }

    [Fact]
    public async Task PingYHealthNoRequierenToken()
    {
        var cliente = Fabrica.CrearCliente();

        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/api/v1/ping")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/swagger/v1/swagger.json")).StatusCode);
    }
}
