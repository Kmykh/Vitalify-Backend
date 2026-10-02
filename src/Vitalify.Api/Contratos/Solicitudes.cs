namespace Vitalify.Api.Contratos;

/// <summary>Credenciales para iniciar sesión.</summary>
/// <param name="Correo">Correo registrado.</param>
/// <param name="Contrasena">Contraseña.</param>
public sealed record LoginSolicitud(string Correo, string Contrasena);

/// <summary>Refresh token recibido en el login o en el último refresh.</summary>
public sealed record RefrescarSolicitud(string RefreshToken);

/// <summary>Opcionalmente, el refresh token de la sesión a cerrar.</summary>
public sealed record CerrarSesionSolicitud(string? RefreshToken);

/// <summary>Datos de la cuenta a registrar.</summary>
/// <param name="Nombre">Nombre completo.</param>
/// <param name="Correo">Correo, único en el sistema.</param>
/// <param name="Contrasena">Al menos 8 caracteres, con al menos una letra y un número.</param>
/// <param name="Rol"><c>Medico</c> o <c>Enfermera</c>.</param>
public sealed record RegistrarUsuarioSolicitud(string Nombre, string Correo, string Contrasena, string Rol);

/// <summary>Cama del catálogo.</summary>
/// <param name="Codigo">Código único, por ejemplo <c>MED-B-05</c>.</param>
/// <param name="Servicio">Servicio hospitalario, por ejemplo "Medicina B".</param>
public sealed record RegistrarCamaSolicitud(string Codigo, string Servicio);

/// <summary>Wearable ESP32.</summary>
/// <param name="Codigo">Código único del tópico MQTT <c>device/{codigo}/telemetria</c>, por ejemplo <c>ESP32-001</c>.</param>
/// <param name="Descripcion">Opcional.</param>
public sealed record RegistrarDispositivoSolicitud(string Codigo, string? Descripcion);

/// <param name="Estado"><c>Mantenimiento</c>, <c>DadoDeBaja</c> o <c>Disponible</c> (reactivar).</param>
public sealed record CambiarEstadoDispositivoSolicitud(string Estado);

/// <summary>Ingreso de un paciente (HU05).</summary>
/// <param name="NombreCompleto">Entre 3 y 150 caracteres.</param>
/// <param name="TipoDocumento"><c>Dni</c>, <c>CarneExtranjeria</c> o <c>Pasaporte</c>.</param>
/// <param name="NumeroDocumento">DNI de 8 dígitos; los demás, de 6 a 12 letras o números.</param>
/// <param name="FechaNacimiento"><c>AAAA-MM-DD</c>. Si no se conoce, enviar <c>edad</c>.</param>
/// <param name="Edad">De 0 a 120. Se estima el nacimiento el 1 de enero del año correspondiente.</param>
/// <param name="CamaId">Cama activa y libre (ver <c>GET /camas?soloDisponibles=true</c>).</param>
/// <param name="DiagnosticoIngreso">Entre 3 y 500 caracteres.</param>
public sealed record RegistrarIngresoSolicitud(
    string NombreCompleto, string TipoDocumento, string NumeroDocumento, string? FechaNacimiento, int? Edad, Guid? CamaId,
    string DiagnosticoIngreso);

/// <summary>Corrección de los datos básicos del paciente y del diagnóstico de su hospitalización activa.</summary>
public sealed record ActualizarPacienteSolicitud(string NombreCompleto, string? FechaNacimiento, int? Edad, string DiagnosticoIngreso);

/// <param name="DispositivoId">Sensor disponible (ver <c>GET /dispositivos?estado=Disponible</c>).</param>
public sealed record VincularDispositivoSolicitud(Guid? DispositivoId);

/// <summary>Lo que el wearable no mide (o una temperatura de termómetro clínico). Al menos un valor.</summary>
/// <param name="Fr">Frecuencia respiratoria (rpm).</param>
/// <param name="Pas">Presión sistólica (mmHg); va con <c>pad</c>.</param>
/// <param name="Pad">Presión diastólica (mmHg); va con <c>pas</c>.</param>
/// <param name="Temperatura">°C, de un termómetro clínico.</param>
/// <param name="Conciencia"><c>Alerta</c>, <c>ConfusionNueva</c>, <c>RespondeVoz</c>, <c>RespondeDolor</c> o <c>NoResponde</c>.</param>
/// <param name="OxigenoSuplementario">true si recibe oxígeno.</param>
/// <param name="ObservadaEn">Momento de la medición (UTC); si falta, ahora. Hasta 4 h atrás.</param>
public sealed record RegistrarObservacionSolicitud(
    int? Fr, int? Pas, int? Pad, decimal? Temperatura, string? Conciencia, bool? OxigenoSuplementario, DateTime? ObservadaEn);

/// <param name="Motivo"><c>AltaMedica</c>, <c>Traslado</c>, <c>Fallecimiento</c>, <c>Voluntaria</c> u <c>Otro</c>.</param>
/// <param name="Observacion">Opcional, hasta 500 caracteres.</param>
public sealed record RegistrarEgresoSolicitud(string Motivo, string? Observacion);
