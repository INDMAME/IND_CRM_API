---
name: safe-change-loop
description: Use when implementing non-trivial code, configuration, contract, data, security, backend, frontend, or architecture changes in IND projects.
---

# Safe Change Loop

## Proposito

Implementa cambios no triviales con autonomia por defecto, estabilidad y evidencia suficiente. El objetivo es hacer el cambio minimo seguro y mantenible sin convertir cada tarea en una ceremonia.

No uses `/plan` ni cambies de modo por iniciativa propia. Usa `/goal` solo si el usuario lo pide o si el trabajo es largo, multi-etapa y tiene una condicion de parada verificable.

## Cuando Activarla

Activala para cambios que toquen comportamiento de produccion, arquitectura, capas, integraciones, configuracion funcional, runtime, build, seguridad, datos, autenticacion, autorizacion, APIs, DTOs, schemas, OpenAPI, Swagger, UI con llamadas API o refactorizaciones con riesgo de regresion.

No la actives para preguntas, explicaciones, revisiones sin edicion, correcciones mecanicas de formato, cambios triviales de texto o tareas expresamente excluidas por el usuario.

## Bucle Autonomo

1. Lee las instrucciones locales aplicables y respeta la regla mas especifica.
2. Revisa `git status` y el diff relevante antes de editar.
3. Protege cambios preexistentes del usuario; no los reviertas ni reformatees.
4. Entiende el flujo actual y los contratos afectados antes de cambiarlo.
5. Clasifica internamente el riesgo: local, interno, estructural, contractual u operacional.
6. Elige la solucion compatible mas pequena que resuelva el requisito.
7. Implementa una porcion coherente.
8. Valida de estrecho a amplio con comandos reales del proyecto.
9. Revisa el diff final y corrige regresiones acotadas.
10. Cierra con evidencia concreta y limitaciones reales.

## Autonomia Por Defecto

No pidas confirmacion para decisiones locales, reversibles o razonablemente inferibles. Si falta un dato no critico, adopta el supuesto conservador, continua y declaralo en el cierre.

No preguntes por rutina para:

- elegir archivos, nombres internos o ubicaciones que siguen patrones existentes;
- ejecutar comandos locales de lectura, build, lint, test o inspeccion;
- reducir el alcance a la menor solucion segura;
- actualizar documentacion relacionada con el cambio;
- usar o no usar MCP cuando la evidencia local basta;
- omitir validacion amplia si el cambio es local y la validacion estrecha es suficiente.

Pausa y pregunta solo ante un hard-stop real:

- accion destructiva, irreversible, externa o sobre produccion;
- riesgo razonable de perdida o corrupcion de datos;
- secretos, permisos, autenticacion, autorizacion o seguridad sensible;
- breaking change contractual inevitable sin alternativa compatible;
- necesidad de modificar fuera del alcance pedido;
- conflicto irresoluble entre instrucciones activas;
- cambios preexistentes del usuario que bloquean el archivo o la logica a modificar;
- imposibilidad razonable de validar comportamiento critico;
- validacion decisiva que solo puede ejecutar el usuario en un runtime externo o manual, por ejemplo AX/Axapta, ERP, cliente GUI, credenciales no disponibles o entorno productivo controlado;
- mismo fallo esencial tras tres intentos con nueva evidencia.

Al pausar, entrega lo confirmado, lo pendiente, la evidencia y la decision exacta que necesitas.

## Contratos Productor-Consumidor

Para endpoints, DTOs, schemas, serializacion, payloads, eventos, headers, errores, envelopes o documentacion contractual:

- identifica productor, consumidores y fuente canonica local;
- preserva compatibilidad hacia atras por defecto;
- prefiere cambios aditivos o adaptadores antes que breaking changes;
- si romper compatibilidad es inevitable y no fue pedido, pausa y pide decision;
- valida ambos lados cuando sea practico;
- documenta en el cierre el impacto contractual o la evidencia de ausencia de impacto.

## Uso De Herramientas Y MCP

No hagas inventario de MCP por rutina. Usa MCP, navegador, documentacion oficial o herramientas externas solo cuando reduzcan una incertidumbre concreta que no se pueda resolver bien con el repositorio local.

Prioriza evidencia en este orden:

1. codigo ejecutado y pruebas del repositorio;
2. contratos, DTOs y configuracion versionados;
3. documentacion oficial de la version instalada;
4. documentacion interna vigente;
5. inferencias del agente.

Si una afirmacion material depende de inferencia o de informacion externa no comprobada, marcala como **"No verificado por terceros"**.

## Implementacion

Mantente dentro del modulo y del alcance pedido. Evita refactorizaciones especulativas, formateo masivo, renombres no relacionados, dependencias nuevas, cambios de runtime/framework/bitness o cambios globales sin requisito explicito.

Refactoriza solo cuando reduzca riesgo real del cambio, elimine duplicacion material ya tocada, aclare una frontera necesaria o haga viable la validacion sin cambiar comportamiento observable.

## Comentarios Y Documentacion MMS

Para unidades logicas nuevas o materialmente modificadas, agrega o actualiza un comentario util solo cuando ayude a entender responsabilidad, contrato, compatibilidad, efecto lateral, invariante, seguridad, error o limitacion legacy.

Formato canonico cuando el lenguaje admite `//`:

```text
//MMS - <descripcion breve en espanol> - AAAA.MM.DD
```

Antes del primer comentario fechado de la tarea, obtiene la fecha real del entorno. Usa delimitadores nativos en TSX, HTML, CSS, CSHTML u otros formatos. No introduzcas comentarios en JSON estricto, lockfiles, snapshots, codigo generado o dependencias.

No comentes lineas triviales, asignaciones obvias, retornos simples, variables o cierres de bloque. Para una unidad modificada, deja un solo comentario principal y no acumules historial MMS.

La documentacion tecnica nueva o materialmente modificada debe estar en espanol simple, conservando identificadores y terminos tecnicos normalizados.

## Validacion

Ejecuta primero la comprobacion con mejor relacion senal/tiempo:

- lint, formato o analisis de archivos tocados;
- build, compilacion o type-check;
- pruebas unitarias o de caracterizacion del comportamiento modificado;
- pruebas de contrato, integracion, UI o smoke cuando el riesgo lo justifique;
- revision final del diff y de comentarios MMS.

No afirmes que ejecutaste algo si no lo hiciste. Diferencia fallos preexistentes de regresiones nuevas. Si una validacion relevante no puede ejecutarse, explica por que y que evidencia alternativa queda.

## Validacion Manual O Externa

Cuando la validacion decisiva dependa de una accion manual del usuario o de un runtime externo no disponible para Codex, tratala como frontera de validacion, no como objetivo de bucle autonomo.

Detente cuando ya tengas:

- investigacion local suficiente y una hipotesis coherente;
- cambio o artefacto preparado para ejecutar manualmente;
- validaciones locales posibles realizadas, por ejemplo diff, sintaxis, formato, importabilidad o busquedas dirigidas;
- instrucciones concretas para que el usuario ejecute la prueba manual.

No sigas generando variantes, jobs, parches o diagnosticos solo para compensar que no puedes ejecutar esa validacion. Haz como maximo una iteracion nueva por cada resultado manual nuevo aportado por el usuario. Si el resultado manual repite una salida antigua, primero distingue version/importacion/cache/runtime no actualizado antes de cambiar mas codigo.

En el cierre, marca explicitamente `pendiente de validacion manual por el usuario`, da los pasos exactos, los marcadores esperados y la salida que confirmaria el comportamiento. No declares completado, corregido en runtime ni probado end-to-end hasta recibir esa evidencia manual.

## Cierre

El informe final debe ser breve y util:

- resumen de cambios y archivos afectados;
- validacion ejecutada con resultado real;
- impacto contractual o evidencia de ausencia de impacto;
- limitaciones y riesgos residuales;
- fecha MMS usada si se modificaron comentarios de codigo.

No incluyas ceremonias, inventarios de herramientas ni secciones largas si no aportan decision o evidencia.
