using Vitalify.Application.Sesiones;
using Vitalify.Application.Usuarios;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Application.Tests.Fakes;

/// <summary>Arma los casos de uso sobre los fakes en memoria.</summary>
public sealed class Escenario
{
    public static readonly OpcionesSesion Opciones = new(TimeSpan.FromMinutes(30), TimeSpan.FromHours(12));

    public RelojFalso Reloj { get; } = new();
    public HasherFalso Hasher { get; } = new();
    public GeneradorTokensFalso Generador { get; } = new();
    public RepositorioUsuariosEnMemoria Usuarios { get; } = new();
    public RepositorioSesionesEnMemoria Sesiones { get; } = new();
    public RepositorioTokensRevocadosEnMemoria TokensRevocados { get; } = new();
    public RepositorioAuditoriaEnMemoria Auditoria { get; } = new();
    public UnidadDeTrabajoFalsa UnidadDeTrabajo { get; } = new();

    public RegistrarUsuario RegistrarUsuario() =>
        new(new RegistrarUsuarioValidador(), Usuarios, Auditoria, UnidadDeTrabajo, Hasher, Reloj);

    public IniciarSesion IniciarSesion() =>
        new(new IniciarSesionValidador(), Usuarios, Sesiones, Auditoria, UnidadDeTrabajo, Hasher, Generador, Reloj, Opciones);

    public RefrescarSesion RefrescarSesion() =>
        new(new RefrescarSesionValidador(), Usuarios, Sesiones, Auditoria, UnidadDeTrabajo, Generador, Reloj, Opciones);

    public CerrarSesion CerrarSesion() =>
        new(Sesiones, TokensRevocados, Auditoria, UnidadDeTrabajo, Generador, Reloj);

    public ListarUsuarios ListarUsuarios() => new(new ListarUsuariosValidador(), Usuarios);

    public ObtenerUsuarioActual ObtenerUsuarioActual() => new(Usuarios);

    public Usuario AgregarUsuario(string correo, string contrasena, Rol rol = Rol.Enfermera, string nombre = "Usuario de prueba")
    {
        var usuario = Domain.Usuarios.Usuario.Crear(nombre, Correo.Crear(correo), Hasher.Calcular(contrasena), rol, Reloj.AhoraUtc);
        Usuarios.Agregar(usuario);
        return usuario;
    }

    public async Task<SesionIniciada> IniciarSesionAsync(string correo, string contrasena)
    {
        var resultado = await IniciarSesion().EjecutarAsync(new IniciarSesionComando(correo, contrasena, "10.0.0.1"));
        Assert.True(resultado.EsExito, resultado.EsExito ? null : resultado.Error.Mensaje);
        return resultado.Valor;
    }
}
