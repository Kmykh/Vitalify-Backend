using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Vitalify.Application.Comun;

namespace Vitalify.Api.Controllers;

/// <summary>Traduce los errores de los casos de uso a ProblemDetails.</summary>
internal static class ResultadoHttp
{
    public static ActionResult Problema(this ControllerBase controlador, Error error)
    {
        if (error.Tipo == TipoError.Validacion)
        {
            var estado = new ModelStateDictionary();
            foreach (var (campo, mensajes) in error.Detalles)
            {
                foreach (var mensaje in mensajes)
                {
                    estado.AddModelError(campo, mensaje);
                }
            }

            return controlador.ValidationProblem(
                title: error.Mensaje, type: error.Codigo, statusCode: StatusCodes.Status400BadRequest, modelStateDictionary: estado);
        }

        var (status, titulo) = error.Tipo switch
        {
            TipoError.Conflicto => (StatusCodes.Status409Conflict, "Conflicto"),
            TipoError.NoAutorizado => (StatusCodes.Status401Unauthorized, "No autorizado"),
            TipoError.NoEncontrado => (StatusCodes.Status404NotFound, "No encontrado"),
            _ => (StatusCodes.Status400BadRequest, "Solicitud inválida"),
        };

        return controlador.Problem(detail: error.Mensaje, statusCode: status, title: titulo, type: error.Codigo);
    }
}
