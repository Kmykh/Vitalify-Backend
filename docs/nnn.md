# Proceso de enfermería con la taxonomía NNN

Vitalify **sugiere** diagnósticos de enfermería; **la enfermera decide**. Esto es apoyo a la decisión, no un diagnóstico automático: nada entra al plan de cuidados sin que una enfermera lo confirme.

NNN reúne tres taxonomías:

| Taxonomía | Qué nombra | Código | Ejemplo |
|---|---|---|---|
| **NANDA-I** | Diagnósticos de enfermería | 5 dígitos | `00032` Patrón respiratorio ineficaz |
| **NIC** | Intervenciones | 4 dígitos | `3350` Monitorización respiratoria |
| **NOC** | Resultados, valorados de 1 a 5 | 4 dígitos (indicadores: 6) | `0415` Estado respiratorio |

## Base normativa

- **Ley N° 27669, Ley del Trabajo de la Enfermera(o)** (15 de febrero de 2002), **artículo 7, inciso a)**: corresponde a la enfermera(o) "Brindar cuidado integral de enfermería basado en el Proceso de Atención de Enfermería (PAE)".
- **Reglamento de la Ley 27669, aprobado por el D.S. N° 004-2002-SA, artículo 9, inciso a)**: precisa que el PAE "incluye la valoración, el diagnóstico, la planificación, la ejecución y la evaluación del mismo, el cual será registrado obligatoriamente en la historia clínica del paciente y/o en la ficha familiar".
  - El D.S. N° 001-2026-SA modificó ese reglamento, pero solo incorporó el literal k) del artículo 11 y el artículo 11-A (consultorios de enfermería). El artículo 9 no cambió.
- **NTS N° 139-MINSA/2018/DGAIN** (gestión de la historia clínica): el registro clínico no se borra. Por eso esta fase no tiene ningún DELETE:
  - un diagnóstico se resuelve o se descarta con motivo;
  - una valoración mal hecha se corrige con otra nueva.

Los cinco pasos del PAE en Vitalify:

| Paso del PAE | Qué hace Vitalify |
|---|---|
| Valoración | Wearable, toma manual, NEWS2 y MEWS (fases 4 y 5) |
| Diagnóstico | El motor sugiere diagnósticos NANDA-I y la enfermera confirma o descarta |
| Planificación | Intervenciones NIC con su frecuencia y resultados NOC con puntuación basal y meta |
| Ejecución | Cada intervención ejecutada queda registrada, también al atender una alerta |
| Evaluación | Valoraciones NOC de 1 a 5 por indicador, con una propuesta a partir de los signos |

## Licencia del catálogo

NANDA-I (Thieme) y NIC/NOC (Elsevier) tienen derechos de autor, y este repositorio es público. Por eso:

- se siembra solo el **código** y una **etiqueta corta** en español, de un **subconjunto pequeño** (14 diagnósticos, 16 intervenciones, 11 resultados y 5 indicadores);
- **nunca** se copian definiciones, características definitorias, factores relacionados, actividades ni textos de los libros;
- cada entrada guarda la **edición** de la que viene: `NANDA-I 2024-2026`, `NIC 8a ed.` o `NOC 7a ed.`;
- el catálogo completo requiere una **licencia del editor** (Thieme para NANDA-I; Elsevier para NIC y NOC). Para usarlo en un hospital, la institución debe obtenerla y cargarlo con el mismo formato de semilla.

Los dominios y clases NANDA-I y los campos NIC de la semilla siguen la estructura que conocemos de las ediciones anteriores. La asesora debe confirmarlos contra las ediciones citadas.

## Verificación de cada entrada

La semilla es **tentativa**. Todas sus entradas quedan con `verificado = false` hasta que una **enfermera asesora** coteja el código y la etiqueta contra su libro:

1. La asesora revisa la entrada en la edición indicada. Por ejemplo, `00303 Riesgo de caídas del adulto`, que en ediciones anteriores era `00155 Riesgo de caídas`.
2. Si la entrada está bien, el administrador la marca con `PUT /api/v1/nnn/catalogo/{tipo}/{codigo}/verificacion`. `tipo` es `nanda`, `nic`, `noc` o `indicador`; queda auditado como `VerificarCatalogoNnn`.
3. Si está mal, se corrige en el JSON de la semilla, se sube la versión del archivo y se vuelve a desplegar.

El indicador de SpO2 (`080208`, en `0802 Signos vitales`) es el más dudoso. Si la asesora no lo encuentra en `0802`, se usa el de `0402 Estado respiratorio: intercambio gaseoso`.

La app muestra `verificado` junto a cada entrada, para que nadie confunda una etiqueta tentativa con la del libro.

## Semilla

- **Archivo:** `src/Vitalify.Infrastructure/Semillas/nnn-catalogo.2026.11.1.json`, un recurso embebido versionado en el nombre y en su campo `version`.
- **Cuándo se carga:** `SemillaCatalogoNnn` lo carga al arrancar, en todos los entornos, porque el catálogo hace falta también en producción.
- **Idempotente:**
  - agrega lo que falta;
  - corrige la etiqueta o la edición de las entradas **sin verificar**;
  - **nunca toca una entrada verificada**;
  - nunca borra: una entrada que sale de la semilla se queda como está.
