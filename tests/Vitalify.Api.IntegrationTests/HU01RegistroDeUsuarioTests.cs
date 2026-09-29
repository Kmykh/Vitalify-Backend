using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Vitalify.Api.IntegrationTests.Infraestructura;
using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Api.IntegrationTests;

/// <summary>HU01 · Registro de usuario con rol asignado.</summary>
public class HU01RegistroDeUsuarioTests(FabricaVitalify fabrica) : PruebaApi(fabrica)
{
    [Fact]
    public async Task HU01_E1_AdministradorRegistraUsuarioConRol_EsePuedeIniciarSesionConElRolAsignado()
    {
        var administrador = await ClienteAdministradorAsync();
        var correo = $"medico.{Guid.NewGuid():N}@vitalify.test";

        var respuesta = await administrador.PostAsJsonAsync("/api/v1/usuarios",
            new { nombre = "Dra. Ana Pérez", correo, contrasena = "Clinica2026", rol = "Medico" });

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var creado = await respuesta.LeerUsuarioAsync();
        Assert.Equal("Medico", creado.Rol);
        Assert.EndsWith($"/api/v1/usuarios/{creado.Id}", respuesta.Headers.Location?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, (await administrador.GetAsync(respuesta.Headers.Location)).StatusCode);

        var sesion = await Fabrica.CrearCliente().IniciarSesionAsync(correo, "Clinica2026");
        Assert.Equal("Medico", sesion.Usuario.Rol);
        Assert.Equal(creado.Id, sesion.Usuario.Id);

        Assert.True(await Fabrica.ConsultarAsync(db => db.Auditoria.AnyAsync(a =>
            a.Accion == AccionAuditoria.UsuarioCreado && a.Detalle!.Contains(creado.Id.ToString()))));
    }

    [Fact]
    public async Task HU01_E2_RegistroConCorreoDuplicado_Devuelve409()
    {
        var administrador = await ClienteAdministradorAsync();
        var correo = await administrador.RegistrarUsuarioAsync("Enfermera");

        var respuesta = await administrador.PostAsJsonAsync("/api/v1/usuarios",
            new { nombre = "Otra persona", correo = correo.ToUpperInvariant(), contrasena = "Clinica2026", rol = "Medico" });

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        var problema = await respuesta.LeerProblemaAsync();
        Assert.Equal("correo-en-uso", problema.Type);
        Assert.Equal("El correo ya está en uso.", problema.Detail);
    }

    [Fact]
    public async Task RegistroSimultaneoConElMismoCorreo_SoloSeCreaUnaCuenta()
    {
        var administrador = await ClienteAdministradorAsync();
        var correo = $"simultaneo.{Guid.NewGuid():N}@vitalify.test";

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            administrador.PostAsJsonAsync("/api/v1/usuarios",
                new { nombre = "Simultáneo", correo, contrasena = "Clinica2026", rol = "Enfermera" })));

        Assert.Single(respuestas, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(respuestas.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(1, await Fabrica.ConsultarAsync(db => db.Usuarios.CountAsync(u => u.Correo == Correo.Crear(correo))));
    }

    [Fact]
    public async Task ViolacionDelIndiceUnicoDeCorreoAlGuardar_SeTraduceAConflicto()
    {
        var correo = Correo.Crear($"indice.{Guid.NewGuid():N}@vitalify.test");

        async Task<Resultado<Unidad>> GuardarEnOtroScopeAsync()
        {
            await using var scope = Fabrica.Services.CreateAsyncScope();
            var servicios = scope.ServiceProvider;
            servicios.GetRequiredService<IRepositorioUsuarios>()
                .Agregar(Usuario.Crear("Índice", correo, "hash", Rol.Enfermera, DateTime.UtcNow));
            return await servicios.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync();
        }

        Assert.True((await GuardarEnOtroScopeAsync()).EsExito);
        var segundo = await GuardarEnOtroScopeAsync();

        Assert.False(segundo.EsExito);
        Assert.Equal(TipoError.Conflicto, segundo.Error.Tipo);
        Assert.Equal("correo-en-uso", segundo.Error.Codigo);
    }

    [Fact]
    public async Task NoSePuedeRegistrarUnAdministradorPorLaApi()
    {
        var respuesta = await (await ClienteAdministradorAsync()).PostAsJsonAsync("/api/v1/usuarios",
            new { nombre = "Otro admin", correo = $"admin.{Guid.NewGuid():N}@vitalify.test", contrasena = "Clinica2026", rol = "Administrador" });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var problema = await respuesta.LeerProblemaAsync();
        Assert.Equal("validacion", problema.Type);
        Assert.Contains("rol", problema.Extensions["errors"]!.ToString());
    }

    [Fact]
    public async Task UnaContrasenaDebil_Devuelve400ConElMensajeDeLaPolitica()
    {
        var respuesta = await (await ClienteAdministradorAsync()).PostAsJsonAsync("/api/v1/usuarios",
            new { nombre = "Débil", correo = $"debil.{Guid.NewGuid():N}@vitalify.test", contrasena = "12345678", rol = "Medico" });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Contains("al menos una letra y un número", await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ElListadoDeUsuariosEsPaginado()
    {
        var administrador = await ClienteAdministradorAsync();
        await administrador.RegistrarUsuarioAsync("Medico");
        await administrador.RegistrarUsuarioAsync("Enfermera");

        var respuesta = await administrador.GetAsync("/api/v1/usuarios?pagina=1&tamano=2");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var pagina = (await respuesta.Content.ReadFromJsonAsync<Contratos.PaginaRespuesta<Application.Usuarios.UsuarioDetalleDto>>())!;
        Assert.Equal(1, pagina.Pagina);
        Assert.Equal(2, pagina.Tamano);
        Assert.Equal(2, pagina.Elementos.Count);
        Assert.True(pagina.Total >= 3);
    }
}
