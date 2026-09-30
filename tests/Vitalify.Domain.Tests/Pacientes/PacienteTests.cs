using Vitalify.Domain.Comun;
using Vitalify.Domain.Pacientes;

namespace Vitalify.Domain.Tests.Pacientes;

public class PacienteTests
{
    private static readonly DateTime Ahora = new(2026, 9, 30, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Hoy = DateOnly.FromDateTime(Ahora);

    [Theory]
    [InlineData("1980-09-30", 46)]
    [InlineData("1980-10-01", 45)]
    [InlineData("1980-09-29", 46)]
    [InlineData("2026-09-30", 0)]
    [InlineData("2000-02-29", 26)]
    public void La_edad_cuenta_si_ya_paso_el_cumpleanos(string nacimiento, int esperada)
    {
        Assert.Equal(esperada, Paciente.CalcularEdad(DateOnly.Parse(nacimiento), Hoy));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(120)]
    public void La_fecha_estimada_por_edad_es_el_1_de_enero_y_devuelve_esa_edad(int edad)
    {
        var fecha = Paciente.FechaEstimadaDesdeEdad(edad, Hoy);

        Assert.Equal(new DateOnly(Hoy.Year - edad, 1, 1), fecha);
        Assert.Equal(edad, Paciente.CalcularEdad(fecha, Hoy));
    }

    [Fact]
    public void Registrar_normaliza_nombre_y_documento()
    {
        var paciente = Paciente.Registrar("  Ana Quispe  ", TipoDocumento.Pasaporte, " ab123456 ", new DateOnly(1990, 5, 20), false, Ahora);

        Assert.Equal("Ana Quispe", paciente.NombreCompleto);
        Assert.Equal("AB123456", paciente.NumeroDocumento);
        Assert.Equal(36, paciente.Edad(Hoy));
        Assert.False(paciente.FechaNacimientoEstimada);
        Assert.Equal(Ahora, paciente.CreadoEn);
    }

    [Theory]
    [InlineData(TipoDocumento.Dni, "12345678", true)]
    [InlineData(TipoDocumento.Dni, "1234567", false)]
    [InlineData(TipoDocumento.Dni, "1234567A", false)]
    [InlineData(TipoDocumento.CarneExtranjeria, "001234567", true)]
    [InlineData(TipoDocumento.CarneExtranjeria, "12345", false)]
    [InlineData(TipoDocumento.Pasaporte, "AB1234567890", true)]
    [InlineData(TipoDocumento.Pasaporte, "AB12345678901", false)]
    [InlineData(TipoDocumento.Pasaporte, "AB-12345", false)]
    public void Valida_el_documento_segun_su_tipo(TipoDocumento tipo, string numero, bool valido)
    {
        Assert.Equal(valido, Paciente.DocumentoValido(tipo, numero));
    }

    [Theory]
    [InlineData("Al")]
    [InlineData("   ")]
    public void El_nombre_debe_tener_al_menos_3_caracteres(string nombre)
    {
        Assert.Throws<ExcepcionDeDominio>(() =>
            Paciente.Registrar(nombre, TipoDocumento.Dni, "12345678", new DateOnly(1990, 1, 1), false, Ahora));
    }

    [Fact]
    public void La_fecha_de_nacimiento_no_puede_ser_futura_ni_superar_120_anos()
    {
        Assert.Throws<ExcepcionDeDominio>(() =>
            Paciente.Registrar("Ana Quispe", TipoDocumento.Dni, "12345678", Hoy.AddDays(1), false, Ahora));
        Assert.Throws<ExcepcionDeDominio>(() =>
            Paciente.Registrar("Ana Quispe", TipoDocumento.Dni, "12345678", Hoy.AddYears(-121), false, Ahora));
    }

    [Fact]
    public void Un_documento_invalido_se_rechaza()
    {
        Assert.Throws<ExcepcionDeDominio>(() =>
            Paciente.Registrar("Ana Quispe", TipoDocumento.Dni, "ABC", new DateOnly(1990, 1, 1), false, Ahora));
    }

    [Fact]
    public void Actualizar_datos_cambia_nombre_fecha_y_marca_de_estimacion()
    {
        var paciente = Paciente.Registrar("Ana Quispe", TipoDocumento.Dni, "12345678", new DateOnly(1990, 1, 1), true, Ahora);

        paciente.ActualizarDatos("Ana María Quispe", new DateOnly(1990, 6, 15), false, Ahora.AddDays(1));

        Assert.Equal("Ana María Quispe", paciente.NombreCompleto);
        Assert.Equal(new DateOnly(1990, 6, 15), paciente.FechaNacimiento);
        Assert.False(paciente.FechaNacimientoEstimada);
        Assert.Equal(Ahora.AddDays(1), paciente.ActualizadoEn);
    }
}
