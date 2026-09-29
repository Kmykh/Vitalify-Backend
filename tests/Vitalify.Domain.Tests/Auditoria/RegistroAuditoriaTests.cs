using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Comun;

namespace Vitalify.Domain.Tests.Auditoria;

public class RegistroAuditoriaTests
{
    [Fact]
    public void Registrar_guarda_los_datos_del_evento()
    {
        var usuarioId = Guid.NewGuid();
        var fecha = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

        var registro = RegistroAuditoria.Registrar(AccionAuditoria.AccesoDenegado, fecha, usuarioId, "10.0.0.1", "GET /api/v1/usuarios");

        Assert.NotEqual(Guid.Empty, registro.Id);
        Assert.Equal(AccionAuditoria.AccesoDenegado, registro.Accion);
        Assert.Equal(usuarioId, registro.UsuarioId);
        Assert.Equal("10.0.0.1", registro.Ip);
        Assert.Equal(fecha, registro.Fecha);
        Assert.Equal("GET /api/v1/usuarios", registro.Detalle);
    }

    [Fact]
    public void Recorta_el_detalle_largo()
    {
        var registro = RegistroAuditoria.Registrar(AccionAuditoria.LoginFallido, DateTime.UtcNow, detalle: new string('x', 5000));
        Assert.Equal(RegistroAuditoria.LargoMaximoDetalle, registro.Detalle!.Length);
    }

    [Fact]
    public void Rechaza_acciones_no_definidas()
    {
        Assert.Throws<ExcepcionDeDominio>(() => RegistroAuditoria.Registrar((AccionAuditoria)99, DateTime.UtcNow));
    }
}
