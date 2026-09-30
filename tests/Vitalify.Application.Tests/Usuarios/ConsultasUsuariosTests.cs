using Vitalify.Application.Comun;
using Vitalify.Application.Tests.Fakes;
using Vitalify.Application.Usuarios;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Application.Tests.Usuarios;

public class ConsultasUsuariosTests
{
    private readonly Escenario _e = new();

    [Fact]
    public async Task Listar_devuelve_la_pagina_pedida_con_el_total()
    {
        foreach (var n in new[] { "Carla", "Ana", "Beto", "Diego", "Elena" })
        {
            _e.AgregarUsuario($"{n.ToLowerInvariant()}@hospital.pe", "Segura123", Rol.Medico, n);
        }

        var resultado = await _e.ListarUsuarios().EjecutarAsync(new ListarUsuariosConsulta(Pagina: 2, Tamano: 2));

        Assert.True(resultado.EsExito);
        Assert.Equal(["Carla", "Diego"], resultado.Valor.Elementos.Select(u => u.Nombre));
        Assert.Equal(5, resultado.Valor.Total);
        Assert.Equal(3, resultado.Valor.TotalPaginas);
    }

    [Theory]
    [InlineData(0, 20, "pagina")]
    [InlineData(1, 0, "tamano")]
    [InlineData(1, 101, "tamano")]
    public async Task Listar_valida_la_paginacion(int pagina, int tamano, string campo)
    {
        var resultado = await _e.ListarUsuarios().EjecutarAsync(new ListarUsuariosConsulta(pagina, tamano));

        Assert.False(resultado.EsExito);
        Assert.Equal(TipoError.Validacion, resultado.Error.Tipo);
        Assert.Contains(campo, resultado.Error.Detalles.Keys);
    }

    [Fact]
    public async Task Obtener_usuario_actual_devuelve_sus_datos()
    {
        var usuario = _e.AgregarUsuario("medico@hospital.pe", "Segura123", Rol.Medico, "Dr. Luis Soto");

        var resultado = await _e.ObtenerUsuarioActual().EjecutarAsync(usuario.Id);

        Assert.Equal(new UsuarioDto(usuario.Id, "Dr. Luis Soto", "medico@hospital.pe", "Medico"), resultado.Valor);
    }

    [Fact]
    public async Task Obtener_usuario_actual_inexistente_devuelve_no_encontrado()
    {
        var resultado = await _e.ObtenerUsuarioActual().EjecutarAsync(Guid.NewGuid());

        Assert.Equal(TipoError.NoEncontrado, resultado.Error.Tipo);
    }
}
