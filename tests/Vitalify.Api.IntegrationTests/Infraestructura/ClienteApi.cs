using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Vitalify.Application.Sesiones;
using Vitalify.Application.Usuarios;

namespace Vitalify.Api.IntegrationTests.Infraestructura;

/// <summary>Atajos para llamar a la API desde las pruebas.</summary>
public static class ClienteApi
{
    public const string ContrasenaDePrueba = "Clinica2026";

    public static Task<HttpResponseMessage> LoginAsync(this HttpClient cliente, string correo, string contrasena) =>
        cliente.PostAsJsonAsync("/api/v1/auth/login", new { correo, contrasena });

    public static Task<HttpResponseMessage> RefrescarAsync(this HttpClient cliente, string refreshToken) =>
        cliente.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken });

    /// <summary>Inicia sesión, verifica el 200 y deja el access token puesto en el cliente.</summary>
    public static async Task<SesionIniciada> IniciarSesionAsync(this HttpClient cliente, string correo, string contrasena)
    {
        var respuesta = await cliente.LoginAsync(correo, contrasena);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var sesion = (await respuesta.Content.ReadFromJsonAsync<SesionIniciada>())!;
        cliente.UsarToken(sesion.AccessToken);
        return sesion;
    }

    public static void UsarToken(this HttpClient cliente, string accessToken) =>
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    /// <summary>Con un cliente de administrador, registra una cuenta con correo único y devuelve su correo.</summary>
    public static async Task<string> RegistrarUsuarioAsync(this HttpClient administrador, string rol)
    {
        var correo = $"{rol.ToLowerInvariant()}.{Guid.NewGuid():N}@vitalify.test";
        var respuesta = await administrador.PostAsJsonAsync("/api/v1/usuarios",
            new { nombre = $"{rol} de prueba", correo, contrasena = ContrasenaDePrueba, rol });
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return correo;
    }

    public static async Task<ProblemDetails> LeerProblemaAsync(this HttpResponseMessage respuesta)
    {
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        return (await respuesta.Content.ReadFromJsonAsync<ProblemDetails>())!;
    }

    public static async Task<UsuarioDetalleDto> LeerUsuarioAsync(this HttpResponseMessage respuesta) =>
        (await respuesta.Content.ReadFromJsonAsync<UsuarioDetalleDto>())!;
}

/// <summary>Base de las pruebas: fábrica compartida y un cliente con sesión de administrador.</summary>
[Collection(ColeccionApi.Nombre)]
public abstract class PruebaApi(FabricaVitalify fabrica)
{
    protected FabricaVitalify Fabrica { get; } = fabrica;

    protected async Task<HttpClient> ClienteAdministradorAsync()
    {
        var cliente = Fabrica.CrearCliente();
        await cliente.IniciarSesionAsync(FabricaVitalify.AdminCorreo, FabricaVitalify.AdminContrasena);
        return cliente;
    }

    /// <summary>Crea una cuenta con el rol indicado y devuelve un cliente con su sesión iniciada.</summary>
    protected async Task<(HttpClient Cliente, SesionIniciada Sesion)> ClienteConRolAsync(string rol)
    {
        var correo = await (await ClienteAdministradorAsync()).RegistrarUsuarioAsync(rol);
        var cliente = Fabrica.CrearCliente();
        var sesion = await cliente.IniciarSesionAsync(correo, ClienteApi.ContrasenaDePrueba);
        return (cliente, sesion);
    }
}
