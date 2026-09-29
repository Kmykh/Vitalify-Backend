using Vitalify.Domain.Comun;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Domain.Tests.Usuarios;

public class UsuarioTests
{
    private static readonly DateTime Ahora = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Correo CorreoValido = Correo.Crear("medico@hospital.pe");

    [Fact]
    public void Crear_asigna_datos_y_queda_activo()
    {
        var usuario = Usuario.Crear("  Dra. Ana Pérez ", CorreoValido, "hash", Rol.Medico, Ahora);

        Assert.NotEqual(Guid.Empty, usuario.Id);
        Assert.Equal("Dra. Ana Pérez", usuario.Nombre);
        Assert.Equal(CorreoValido, usuario.Correo);
        Assert.Equal("hash", usuario.HashContrasena);
        Assert.Equal(Rol.Medico, usuario.Rol);
        Assert.True(usuario.Activo);
        Assert.Equal(Ahora, usuario.CreadoEn);
        Assert.Equal(Ahora, usuario.ActualizadoEn);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void El_nombre_es_obligatorio(string nombre)
    {
        Assert.Throws<ExcepcionDeDominio>(() => Usuario.Crear(nombre, CorreoValido, "hash", Rol.Medico, Ahora));
    }

    [Fact]
    public void El_nombre_no_puede_superar_el_largo_maximo()
    {
        var nombre = new string('a', Usuario.LargoMaximoNombre + 1);
        Assert.Throws<ExcepcionDeDominio>(() => Usuario.Crear(nombre, CorreoValido, "hash", Rol.Medico, Ahora));
    }

    [Fact]
    public void El_hash_de_la_contrasena_es_obligatorio()
    {
        Assert.Throws<ExcepcionDeDominio>(() => Usuario.Crear("Ana", CorreoValido, " ", Rol.Medico, Ahora));
    }

    [Fact]
    public void El_rol_debe_ser_uno_de_los_definidos()
    {
        Assert.Throws<ExcepcionDeDominio>(() => Usuario.Crear("Ana", CorreoValido, "hash", (Rol)99, Ahora));
    }

    [Fact]
    public void Desactivar_marca_inactivo_y_actualiza_la_fecha()
    {
        var usuario = Usuario.Crear("Ana", CorreoValido, "hash", Rol.Enfermera, Ahora);

        usuario.Desactivar(Ahora.AddHours(1));

        Assert.False(usuario.Activo);
        Assert.Equal(Ahora.AddHours(1), usuario.ActualizadoEn);
    }
}
