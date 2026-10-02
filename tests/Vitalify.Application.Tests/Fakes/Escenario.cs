using Vitalify.Application.Camas;
using Vitalify.Application.Clinica;
using Vitalify.Application.Dispositivos;
using Vitalify.Application.Pacientes;
using Vitalify.Application.Sesiones;
using Vitalify.Application.Telemetria;
using Vitalify.Application.Usuarios;
using Vitalify.Domain.Camas;
using Vitalify.Domain.Dispositivos;
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

    public RepositorioPacientesEnMemoria Pacientes { get; } = new();
    public RepositorioHospitalizacionesEnMemoria Hospitalizaciones { get; } = new();
    public RepositorioCamasEnMemoria Camas { get; }
    public RepositorioDispositivosEnMemoria Dispositivos { get; }

    public Guid EnfermeraId { get; } = Guid.NewGuid();

    public ResolutorDispositivosFalso Resolutor { get; }
    public HistorialEnMemoria Historial { get; } = new();
    public SolicitudNuevaLecturaFalsa SolicitudNuevaLectura { get; } = new();
    public ManejadorLecturaRegistradaFalso Manejador { get; } = new();
    public PresenciaEnMemoria Presencia { get; } = new();
    public OpcionesClinicas Clinicas { get; set; } = OpcionesClinicas.PorDefecto;

    public Escenario()
    {
        Camas = new RepositorioCamasEnMemoria(Hospitalizaciones.Ocupada);
        Dispositivos = new RepositorioDispositivosEnMemoria(Hospitalizaciones.CamaDe);
        Hospitalizaciones.Pacientes = Pacientes;
        Hospitalizaciones.Camas = Camas;
        Hospitalizaciones.Dispositivos = Dispositivos;
        Resolutor = new ResolutorDispositivosFalso(Hospitalizaciones, Dispositivos);
    }

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

    public RegistrarCama RegistrarCama() => new(new RegistrarCamaValidador(), Camas, UnidadDeTrabajo);

    public ListarCamas ListarCamas() => new(Camas);

    public RegistrarDispositivo RegistrarDispositivo() => new(new RegistrarDispositivoValidador(), Dispositivos, UnidadDeTrabajo);

    public CambiarEstadoDispositivo CambiarEstadoDispositivo() =>
        new(new CambiarEstadoDispositivoValidador(), Dispositivos, UnidadDeTrabajo);

    public ListarDispositivos ListarDispositivos() => new(Dispositivos);

    public RegistrarIngresoPaciente RegistrarIngresoPaciente() =>
        new(new RegistrarIngresoPacienteValidador(Reloj), Pacientes, Camas, Hospitalizaciones, Auditoria, UnidadDeTrabajo, Reloj);

    public ActualizarDatosPaciente ActualizarDatosPaciente() =>
        new(new ActualizarDatosPacienteValidador(Reloj), Pacientes, Hospitalizaciones, Auditoria, UnidadDeTrabajo, Reloj);

    public VincularDispositivo VincularDispositivo() =>
        new(Pacientes, Dispositivos, Camas, Hospitalizaciones, Auditoria, UnidadDeTrabajo, Resolutor, Reloj);

    public LiberarDispositivo LiberarDispositivo() =>
        new(Pacientes, Dispositivos, Hospitalizaciones, Auditoria, UnidadDeTrabajo, Resolutor, Reloj);

    public RegistrarEgreso RegistrarEgreso() =>
        new(new RegistrarEgresoValidador(), Pacientes, Dispositivos, Hospitalizaciones, Auditoria, UnidadDeTrabajo, Resolutor, Reloj);

    public ListarPacientesMonitoreados ListarPacientesMonitoreados() =>
        new(Hospitalizaciones, Historial, Historial, Presencia, OpcionesTelemetria.PorDefecto, Reloj);

    /// <summary>Con el motor clínico enganchado, como en la API.</summary>
    public RegistrarLectura RegistrarLectura() =>
        new(Resolutor, Historial, Historial, Historial, Historial, SolicitudNuevaLectura,
            [Manejador, new EvaluarRiesgoAlRegistrarLectura(Evaluador(), Historial, Historial)],
            Domain.Telemetria.RangosFisiologicos.PorDefecto, OpcionesTelemetria.PorDefecto, Reloj);

    public ObtenerSignosActuales ObtenerSignosActuales() =>
        new(Pacientes, Hospitalizaciones, Historial, Presencia, OpcionesTelemetria.PorDefecto, Reloj);

    public EvaluadorRiesgo Evaluador() => new(Historial, Historial, Historial, OpcionesTelemetria.PorDefecto, Clinicas, Reloj);

    public RegistrarObservacion RegistrarObservacion() =>
        new(new RegistrarObservacionValidador(Domain.Telemetria.RangosFisiologicos.PorDefecto, Clinicas, Reloj),
            Pacientes, Hospitalizaciones, Historial, Evaluador(), Historial, Auditoria, UnidadDeTrabajo,
            Domain.Telemetria.RangosFisiologicos.PorDefecto, Reloj);

    public ObtenerRiesgoPaciente ObtenerRiesgoPaciente() => new(Pacientes, Hospitalizaciones, Historial);

    public RegistrarPresenciaDispositivo RegistrarPresencia() => new(Presencia, Reloj);

    public ListarIncidencias ListarIncidencias() => new(new ListarIncidenciasValidador(), Historial);

    /// <summary>Lectura del contrato con marca de tiempo relativa a la hora actual del reloj falso.</summary>
    public LecturaTelemetria Lectura(Domain.Telemetria.SignosMedidos signos, string dispositivo = "ESP32-001", int segundos = 0, bool caida = false) =>
        new(dispositivo, Reloj.AhoraUtc.AddSeconds(segundos), 1, signos, caida, Domain.Telemetria.OrigenLectura.ApiDesarrollo);

    public ListarPacientesHospitalizados ListarPacientesHospitalizados() =>
        new(new ListarPacientesHospitalizadosValidador(), Hospitalizaciones, Reloj);

    public ObtenerPaciente ObtenerPaciente() => new(Pacientes, Hospitalizaciones, Auditoria, UnidadDeTrabajo, Reloj);

    public ObtenerHospitalizacionActivaPorDispositivo ObtenerHospitalizacionActivaPorDispositivo() => new(Hospitalizaciones);

    public Cama AgregarCama(string codigo = "MED-B-01")
    {
        var cama = Cama.Registrar(codigo, "Medicina B");
        Camas.Agregar(cama);
        return cama;
    }

    public Dispositivo AgregarDispositivo(string codigo = "ESP32-001")
    {
        var dispositivo = Dispositivo.Registrar(codigo, null);
        Dispositivos.Agregar(dispositivo);
        return dispositivo;
    }

    public RegistrarIngresoPacienteComando ComandoIngreso(
        Guid? camaId, string documento = "12345678", string nombre = "Rosa Huamán Quispe",
        string? fechaNacimiento = "1958-03-14", int? edad = null) =>
        new(nombre, "Dni", documento, fechaNacimiento, edad, camaId, "Neumonía adquirida en la comunidad", EnfermeraId, "10.0.0.7");

    /// <summary>Ingresa un paciente en una cama nueva y devuelve su ficha.</summary>
    public async Task<FichaPacienteDto> IngresarAsync(string codigoCama = "MED-B-01", string documento = "12345678")
    {
        var cama = Camas.Camas.SingleOrDefault(c => c.Codigo == codigoCama) ?? AgregarCama(codigoCama);
        var resultado = await RegistrarIngresoPaciente().EjecutarAsync(ComandoIngreso(cama.Id, documento));
        Assert.True(resultado.EsExito, resultado.EsExito ? null : resultado.Error.Mensaje);
        return resultado.Valor;
    }

    public async Task<VinculacionDto> VincularAsync(Guid pacienteId, Dispositivo dispositivo)
    {
        var resultado = await VincularDispositivo().EjecutarAsync(new VincularDispositivoComando(pacienteId, dispositivo.Id, EnfermeraId, null));
        Assert.True(resultado.EsExito, resultado.EsExito ? null : resultado.Error.Mensaje);
        return resultado.Valor;
    }

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
