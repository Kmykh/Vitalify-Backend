using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Vitalify.Application.Camas;
using Vitalify.Application.Dispositivos;
using Vitalify.Application.Pacientes;
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

    private static int _ultimoDni = Random.Shared.Next(10_000_000, 80_000_000);

    /// <summary>DNI único dentro de la ejecución.</summary>
    public static string NuevoDni() => Interlocked.Increment(ref _ultimoDni).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static async Task<CamaDto> CrearCamaAsync(this HttpClient administrador)
    {
        var respuesta = await administrador.PostAsJsonAsync("/api/v1/camas",
            new { codigo = $"T-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}", servicio = "Medicina B" });
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<CamaDto>())!;
    }

    public static async Task<DispositivoDto> CrearDispositivoAsync(this HttpClient administrador)
    {
        var respuesta = await administrador.PostAsJsonAsync("/api/v1/dispositivos",
            new { codigo = $"ESP32-{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}", descripcion = "Wearable de prueba" });
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<DispositivoDto>())!;
    }

    public static Task<HttpResponseMessage> IngresarAsync(this HttpClient enfermera, Guid camaId, string dni, string nombre = "Paciente de Prueba") =>
        enfermera.PostAsJsonAsync("/api/v1/pacientes", new
        {
            nombreCompleto = nombre,
            tipoDocumento = "Dni",
            numeroDocumento = dni,
            fechaNacimiento = "1958-03-14",
            camaId,
            diagnosticoIngreso = "Neumonía adquirida en la comunidad",
        });

    /// <summary>Ingresa un paciente con DNI único en la cama indicada y verifica el 201.</summary>
    public static async Task<FichaPacienteDto> IngresarPacienteAsync(this HttpClient enfermera, Guid camaId, string? dni = null)
    {
        var respuesta = await enfermera.IngresarAsync(camaId, dni ?? NuevoDni());
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<FichaPacienteDto>())!;
    }

    public static Task<HttpResponseMessage> VincularAsync(this HttpClient enfermera, Guid pacienteId, Guid dispositivoId) =>
        enfermera.PostAsJsonAsync($"/api/v1/pacientes/{pacienteId}/dispositivo", new { dispositivoId });

    public static Task<HttpResponseMessage> EgresarAsync(this HttpClient enfermera, Guid pacienteId, string motivo = "AltaMedica") =>
        enfermera.PostAsJsonAsync($"/api/v1/pacientes/{pacienteId}/egreso", new { motivo, observacion = "Evolución favorable" });

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

    protected async Task<HttpClient> ClienteEnfermeraAsync() => (await ClienteConRolAsync("Enfermera")).Cliente;

    protected async Task<HttpClient> ClienteMedicoAsync() => (await ClienteConRolAsync("Medico")).Cliente;

    /// <summary>Crea una cuenta con el rol indicado y devuelve un cliente con su sesión iniciada.</summary>
    protected async Task<(HttpClient Cliente, SesionIniciada Sesion)> ClienteConRolAsync(string rol)
    {
        var correo = await (await ClienteAdministradorAsync()).RegistrarUsuarioAsync(rol);
        var cliente = Fabrica.CrearCliente();
        var sesion = await cliente.IniciarSesionAsync(correo, ClienteApi.ContrasenaDePrueba);
        return (cliente, sesion);
    }
}
