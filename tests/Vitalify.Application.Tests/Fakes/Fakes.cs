using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Sesiones;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Application.Tests.Fakes;

public sealed class RelojFalso : IReloj
{
    public DateTime AhoraUtc { get; set; } = new(2026, 9, 29, 7, 0, 0, DateTimeKind.Utc);

    public void Avanzar(TimeSpan tiempo) => AhoraUtc += tiempo;
}

/// <summary>"Hash" legible para las pruebas. Registra cuántas veces se verificó sin usuario.</summary>
public sealed class HasherFalso : IHasherContrasenas
{
    public int VerificacionesSinUsuario { get; private set; }

    public string Calcular(string contrasena) => "hash:" + contrasena;

    public bool Verificar(string? hash, string contrasena)
    {
        if (hash is null)
        {
            VerificacionesSinUsuario++;
            return false;
        }

        return hash == Calcular(contrasena);
    }
}

public sealed class GeneradorTokensFalso : IGeneradorTokens
{
    private int _contador;

    public TokenDeAcceso GenerarAccessToken(Usuario usuario, DateTime ahora)
    {
        var n = Interlocked.Increment(ref _contador);
        return new TokenDeAcceso($"access-{n}", $"jti-{n}", ahora.AddMinutes(15));
    }

    public string GenerarRefreshToken() => $"refresh-{Interlocked.Increment(ref _contador)}";

    public string CalcularHash(string refreshToken) => "sha:" + refreshToken;
}

public sealed class RepositorioUsuariosEnMemoria : IRepositorioUsuarios
{
    public List<Usuario> Usuarios { get; } = [];

    public Task<Usuario?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Usuarios.SingleOrDefault(u => u.Id == id));

    public Task<Usuario?> ObtenerPorCorreoAsync(Correo correo, CancellationToken ct = default) =>
        Task.FromResult(Usuarios.SingleOrDefault(u => u.Correo == correo));

    public Task<bool> ExisteCorreoAsync(Correo correo, CancellationToken ct = default) =>
        Task.FromResult(Usuarios.Any(u => u.Correo == correo));

    public Task<bool> ExisteConRolAsync(Rol rol, CancellationToken ct = default) =>
        Task.FromResult(Usuarios.Any(u => u.Rol == rol));

    public Task<Pagina<Usuario>> ListarAsync(int pagina, int tamano, CancellationToken ct = default)
    {
        var ordenados = Usuarios.OrderBy(u => u.Nombre).ThenBy(u => u.Id).ToList();
        var elementos = ordenados.Skip((pagina - 1) * tamano).Take(tamano).ToList();
        return Task.FromResult(new Pagina<Usuario>(elementos, pagina, tamano, ordenados.Count));
    }

    public void Agregar(Usuario usuario) => Usuarios.Add(usuario);
}

public sealed class RepositorioSesionesEnMemoria : IRepositorioSesiones
{
    public List<SesionRefresco> Sesiones { get; } = [];

    public Task<SesionRefresco?> ObtenerPorHashAsync(string hashToken, CancellationToken ct = default) =>
        Task.FromResult(Sesiones.SingleOrDefault(s => s.HashToken == hashToken));

    public Task<IReadOnlyList<SesionRefresco>> ListarNoRevocadasAsync(Guid usuarioId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SesionRefresco>>(Sesiones.Where(s => s.UsuarioId == usuarioId && !s.EstaRevocada).ToList());

    public void Agregar(SesionRefresco sesion) => Sesiones.Add(sesion);
}

public sealed class RepositorioTokensRevocadosEnMemoria : IRepositorioTokensRevocados
{
    public List<TokenRevocado> Tokens { get; } = [];

    public Task<bool> EstaRevocadoAsync(string jti, CancellationToken ct = default) =>
        Task.FromResult(Tokens.Any(t => t.Jti == jti));

    public void Agregar(TokenRevocado token) => Tokens.Add(token);
}

public sealed class RepositorioAuditoriaEnMemoria : IRepositorioAuditoria
{
    public List<RegistroAuditoria> Registros { get; } = [];

    public void Agregar(RegistroAuditoria registro) => Registros.Add(registro);

    public IEnumerable<RegistroAuditoria> De(AccionAuditoria accion) => Registros.Where(r => r.Accion == accion);
}

public sealed class UnidadDeTrabajoFalsa : IUnidadDeTrabajo
{
    public int Guardados { get; private set; }

    /// <summary>Si se asigna, el próximo guardado devuelve este error (simula una violación de unicidad).</summary>
    public Error? ErrorAlGuardar { get; set; }

    public Task<Resultado<Unidad>> GuardarCambiosAsync(CancellationToken ct = default)
    {
        Guardados++;
        if (ErrorAlGuardar is { } error)
        {
            ErrorAlGuardar = null;
            return Task.FromResult<Resultado<Unidad>>(error);
        }

        return Task.FromResult<Resultado<Unidad>>(Unidad.Valor);
    }
}
