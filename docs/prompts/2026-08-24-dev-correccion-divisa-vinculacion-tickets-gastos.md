# Prompt DEV: preservar la divisa original al vincular tickets a hojas de gastos

Fecha: 2026-08-24

## Prompt para el agente de implementacion

Trabaja como responsable tecnico de una correccion coordinada entre `IND_CRM_API`, Axapta y el frontend de `IND_CRM_APP`. Implementa y valida la solucion en `DEV`, pero no mezcles ramas, no abras ni completes una promocion a `PROD`, no publiques IIS, no despliegues la API y no importes objetos en el AOT de produccion.

El resultado debe preservar de extremo a extremo el importe original y la divisa real de un ticket al vincularlo a una hoja de gastos. El cliente no debe convertirse en fuente autoritativa de importes.

## Incidente confirmado

Dos reproducciones conocidas son:

- `20260817033753_IJL_F000000054.jpg`
- `20260824095457_MAME_F000000060`

En la segunda, antes de vincular, el ticket tenia aproximadamente:

- Divisa original: `BRL`
- Importe original: `11,50 BRL`
- Importe de reembolso: `1,90 EUR`
- Tipo de cambio AX: `605,26` por cada 100 unidades

La peticion web de vinculacion era intencionadamente minima:

```json
{
  "expenseSheetId": "000366",
  "selectionMode": "selected",
  "ticketIds": ["F000000060"]
}
```

Tras la vinculacion, tanto la linea como el ticket quedaron en `EUR`, importe `11,50`, reembolso `11,50` y tasa `100`. No fue una conversion valida: se perdio el snapshot monetario original.

## Causa raiz ya demostrada

La API recupera correctamente el detalle completo del ticket antes de enlazarlo. En `CrmExpenseSheetTicketsController.TryLinkTicketToExpenseSheet` construye el contenedor de linea AX con ocho posiciones basicas y llama a `AppendLinkedTicketLineCurrencyFields`.

El enlace masivo pasa `fallbackMissingCurrencyValues = false`. El helper detecta que la moneda es extranjera, pero ejecuta un retorno temprano cuando el booleano es `false`. Como consecuencia, no agrega:

- Posicion 9: marcador de valor opcional, actualmente `"null"`.
- Posicion 10: `CurrencyCode`, por ejemplo `BRL`.
- Posicion 11: `AmountMST`, por ejemplo `1,90`.
- Posicion 12: `ExchRate`, por ejemplo `605,26`.

No soluciones esto cambiando simplemente `false` por `true`. El camino `true` actual sustituye datos ausentes por `QuickCreateInsertFallbackAmount`, cuyo valor sintetico puede ser `1`. Eso podria dejar datos monetarios falsos y ocultar tickets incompletos.

Cuando AX recibe solo ocho campos, `INDCRMExpenseSheetService.createExpenseSheet`, modo 2, hereda la divisa EUR de la cabecera de la hoja, normaliza la tasa a 100 y calcula el reembolso como si el importe original estuviera en EUR. Posteriormente, `CRMHojaGastosLine.insert()` llama a `syncLinkedTicket()`, que copia esos valores desde la linea al ticket. Por eso se corrompen ambos registros.

Existe ademas una ruta nativa AX. La clase `INDTicketExpenseSheetLink` debe copiar `CurrencyCode`, `ExchRate` y `AmountMST` del ticket antes de insertar la linea. Verifica la version real de `DEV`: un XPO individual ya contenia esas asignaciones, pero un export consolidado de PROD no las contenia.

## Repositorios y preflight obligatorio

Trabaja exclusivamente con los checkouts `DEV`:

- Aplicacion, frontend y fuentes XPO: `C:\INDProjects\GitHub Projects\IND_CRM_APP\IND_CRM_APP`
- API: `C:\INDProjects\GitHub Projects\IND_CRM_API\IND_CRM_API`

Antes de editar cualquiera de los dos repositorios:

1. Lee todas las instrucciones locales `AGENTS.md` y `.codex` aplicables.
2. Ejecuta `git status --short --branch`, `git worktree list --porcelain`, `git remote -v`, `git log -1 --oneline --decorate` y comprueba la divergencia respecto a `origin/DEV`.
3. Refresca referencias remotas de forma no destructiva si es necesario.
4. Confirma que el trabajo se realiza sobre `DEV`. Si un checkout contiene cambios ajenos, preservalos; no hagas `reset --hard`, `checkout --`, limpieza masiva ni reescrituras.
5. Registra los SHA iniciales de ambos repositorios en el informe final.
6. Inspecciona el historial de los metodos afectados para no eliminar cambios posteriores, especialmente los relacionados con quick ticket, importes ausentes y vinculacion de lineas existentes.

No uses el worktree `IND_CRM_APP_PROD_publish` para implementar esta tarea.

## Alcance permitido

### `IND_CRM_API`

Archivo principal permitido:

- `Controllers/CRM/CrmExpenseSheetTicketsController.cs`

Tambien se permiten archivos de pruebas ya existentes o pruebas nuevas dentro de la infraestructura de tests del repositorio. No agregues paquetes, frameworks ni un proyecto de pruebas nuevo sin justificarlo y pedir autorizacion.

Los contratos se pueden modificar solo si es imprescindible para comunicar un error de forma compatible. La solucion prevista no requiere añadir importes al request bulk.

### `IND_CRM_APP` y Axapta

Objetos XPO permitidos:

- `.codex/Axapta/INDCRMExpenseSheetService.xpo`
- `.codex/Axapta/INDTicketExpenseSheetLink.xpo`

Usa `.codex/Axapta/CRMHojaGastosLine.xpo` para comprender y probar `normalizeCurrencyAmounts` y `syncLinkedTicket`, pero no lo modifiques salvo que aparezca evidencia nueva, concreta y documentada que haga imposible la solucion en las dos clases anteriores. No cambies tablas, EDT, indices ni esquema de base de datos.

Archivos frontend permitidos, solo si el analisis de la UX confirma que son necesarios:

- `Web/wwwroot/react/src/pages/gastos/utils/expenseApi.ts`
- `Web/wwwroot/react/src/pages/gastos/expenseTypes.ts`
- `Web/wwwroot/react/src/pages/gastos/tickets/ExpenseTicketsPage.tsx`
- `Web/wwwroot/react/src/pages/gastos/components/ExpenseTicketLinkBulkSummary.tsx`
- Recursos `App/Resources/Infrastructure/Localization/INDSharedResource*.resx` y el bootstrap Razor correspondiente, solo para nuevas cadenas visibles.

No edites `wwwroot/react/src` en la raiz ni bundles generados de `Web/wwwroot/js` o `Web/wwwroot/js/chunks`.

Si concluyes que hacen falta otros archivos, detente antes de modificarlos y presenta la evidencia y el motivo.

## Diseño requerido en la API

### Separar propagacion valida de politica de fallback

Refactoriza el helper para que estas dos decisiones no dependan del mismo booleano:

1. Propagar siempre un snapshot monetario extranjero valido.
2. Decidir que hacer cuando ese snapshot esta incompleto.

Puede usarse un metodo `Try...` con `out message`, una politica explicita o una estructura equivalente, siempre que el flujo quede inequívoco y sea compatible con la version de C# y .NET Framework del proyecto.

Comportamiento obligatorio:

- Normaliza `CurrencyCode` con `Trim()` y mayusculas.
- Para moneda local `EUR`, conserva el comportamiento actual y no inventes datos opcionales innecesarios.
- Para moneda extranjera, toma el importe original de `TotalAmountCurrency ?? TotalAmount`.
- Toma el reembolso de `TotalAmountMST ?? AmountMST`.
- Toma la tasa de `ExchRate` sin invertirla ni convertirla a una tasa por unidad. AX usa la representacion por 100; `605,26` es correcto para el ejemplo BRL.
- Si `CurrencyCode`, importe original, `AmountMST` o `ExchRate` no existen o no son positivos en el bulk normal, falla ese ticket antes de llamar a AX.
- Un ticket extranjero valido debe producir siempre las posiciones 9 a 12, independientemente de que la politica de fallback sea permisiva o estricta.
- Conserva el fallback legacy del quick-create solo si el historial y las pruebas demuestran que sigue siendo necesario. En tal caso, dale un nombre explicito y limita su uso al flujo quick-create; no permitas que alcance el bulk normal.
- Devuelve un mensaje estable y accionable para el ticket fallido, sin exponer excepciones internas. Ejemplo semantico: `El ticket BRL no tiene importe de reembolso o tipo de cambio valido y no se ha vinculado.`
- Mantiene la semantica bulk actual: el endpoint puede responder correctamente con resultados parciales y debe incluir el ticket invalido en `failed[]`. No conviertas un unico ticket malo en un fallo destructivo de todo el lote.
- No llames a `INDCRMExpenseSheetService.createExpenseSheet` para un ticket que no haya superado la validacion monetaria.

Mantén la recuperacion server-side mediante `TryGetExpenseSheetTicketDetail`. El navegador no debe decidir ni reenviar `TotalAmount`, `AmountMST`, `ExchRate` o `CurrencyCode` como valores autoritativos.

### Observabilidad

Añade o ajusta logging estructurado y acotado para poder correlacionar:

- `traceId`
- `expenseSheetId`
- `ticketId`
- `currencyCode`
- resultado `linked`, `skipped` o `failed`
- motivo de validacion

No registres OCR completo, imagenes, tokens ni datos personales innecesarios. Evita duplicar el mismo evento en varios niveles.

## Defensa requerida en Axapta

### `INDCRMExpenseSheetService.createExpenseSheet`

En modo 2, al añadir lineas a una hoja existente, si la linea lleva un `fileId` que identifica un ticket existente, trata `INDTicketInfoTable` como fuente autoritativa del snapshot monetario.

Antes de inicializar o insertar `CRMHojaGastosLine`:

1. Recupera el ticket con los mismos limites de empresa y propietario/usuario que ya aplica el servicio.
2. Copia desde el ticket:
   - `TotalAmount` al precio e importe original de la linea.
   - `CurrencyCode` a `Currency`.
   - `AmountMST` a `AmountMST`.
   - `ExchRate` a `ExchRate`.
3. No dejes que los campos monetarios ausentes en el contenedor provoquen herencia silenciosa de la divisa de la cabecera.
4. Si el contenedor trae importes distintos a los del ticket, prevalece el registro de ticket. El request no es la fuente de verdad.
5. Para EUR, conserva la normalizacion local: tasa 100 y reembolso coherente con el importe.
6. Para moneda extranjera, exige importe original, `AmountMST` y `ExchRate` positivos. Si el snapshot es invalido, aborta antes del `insert()` con un mensaje controlado.
7. Asegura que el error participa en el `ttsbegin/ttscommit` existente: no puede quedar una linea insertada ni un ticket parcialmente actualizado.
8. Conserva descripcion, fecha, tipo, proyecto, permisos y reglas de estado existentes salvo que una prueba demuestre un defecto relacionado.

No elimines globalmente `CRMHojaGastosLine.syncLinkedTicket()`. Con la linea construida correctamente, esa sincronizacion vuelve a escribir los mismos valores. Cambiarla ampliaria el riesgo sobre ediciones legitimas posteriores.

### `INDTicketExpenseSheetLink.run`

Verifica que la ruta nativa copie, antes de `insert()`:

```x++
hojaGastosLine.Currency  = ticket.CurrencyCode;
hojaGastosLine.ExchRate  = ticket.ExchRate;
hojaGastosLine.AmountMST = ticket.AmountMST;
```

Ademas, aplica la misma validacion defensiva basica que al servicio:

- EUR se normaliza de forma local.
- Una moneda extranjera incompleta no se vincula ni modifica.
- El usuario recibe un mensaje claro.
- No hay escrituras parciales.

No importes un export consolidado grande para resolverlo. Mantén cada objeto en su XPO individual y evita reordenamientos o cambios de encoding no relacionados. Los comentarios nuevos en codigo deben estar en ingles y ASCII, conforme a las instrucciones del proyecto y a las limitaciones del AOT antiguo.

## Responsabilidad del frontend

El frontend no causó la conversion y no debe asumir la responsabilidad monetaria. Conserva el request bulk basado en:

- `expenseSheetId`
- `selectionMode`
- `ticketIds`, o filtros y exclusiones

No agregues a ese request `CurrencyCode`, `TotalAmount`, `AmountMST` ni `ExchRate` como datos autoritativos. Una validacion visual del cliente puede ayudar, pero nunca sustituye la validacion de API/AX.

Revisa el tratamiento de resultados parciales en `ExpenseTicketsPage.tsx`. Actualmente, cuando `linkedCount > 0`, el flujo puede navegar inmediatamente a la hoja incluso si tambien hay fallos, ocultando el resumen detallado. Ajusta la UX con estas reglas:

- Exito completo: puede conservar la navegacion actual.
- Resultado parcial: no ocultes el resumen ni el motivo de los tickets fallidos; no presentes el lote como exito total.
- Mantén identificables los tickets no vinculados. No borres silenciosamente su seleccion antes de que el usuario pueda ver el resultado.
- Todos fallidos: permanece en la lista, muestra el error y no navega.
- Tras refrescar, los datos mostrados deben proceder nuevamente del servidor; no hagas una mutacion optimista de moneda o importes.
- Usa `ExpenseTicketLinkBulkSummary` y el sistema `indT` existente. No añadas texto visible solo en español ni omitas idiomas soportados.
- Asegura estados de carga, foco y anuncio accesible del resumen; evita dobles envios mientras el bulk esta en curso.

Si el comportamiento actual ya satisface una regla, demuestralo con una prueba y no hagas un cambio cosmetico.

## Pruebas obligatorias

### API: pruebas automatizadas o harness reproducible

Como minimo, cubre:

1. Ticket BRL valido, politica estricta: construye 12 posiciones con `BRL`, `1,90` y `605,26`.
2. Ticket extranjero valido con politica legacy: usa igualmente los valores reales, nunca el fallback.
3. Ticket EUR valido: mantiene el contrato local y no cambia el comportamiento previo.
4. BRL sin `AmountMST`: devuelve fallo de validacion antes de invocar AX.
5. BRL sin `ExchRate`: devuelve fallo de validacion antes de invocar AX.
6. Moneda vacia o importe original no positivo: fallo controlado sin invocacion AX.
7. Lote mixto: vincula los validos, informa los invalidos en `failed[]` y mantiene contadores e IDs consistentes.
8. Quick-create: conserva exactamente el comportamiento legacy decidido y no hereda la politica estricta por accidente.
9. Mapeo AX a DTO: `CurrencyCode`, `TotalAmountCurrency`, `AmountMST`, `TotalAmountMST` y `ExchRate` llegan sin intercambio de campos.

Si el repositorio no dispone de infraestructura aislable para COM de Axapta, extrae solo la logica pura minima que permita probar el armado/validacion del snapshot, sin añadir dependencias. Documenta aparte la prueba de integracion que requiere AX.

### Axapta: pruebas de integracion DEV

Ejecuta cada caso por el enlace web y, cuando aplique, por `INDTicketExpenseSheetLink`:

1. BRL: `11,50 BRL`, `1,90 EUR`, tasa AX `605,26`.
2. Otra moneda extranjera con decimales distintos para evitar una solucion codificada solo para BRL.
3. EUR: importe original igual al reembolso y tasa 100.
4. Contenedor legacy de ocho campos: AX recupera los valores reales del ticket y no hereda EUR de la hoja.
5. Contenedor con valores monetarios deliberadamente distintos: AX usa el ticket persistido.
6. Ticket extranjero incompleto: no se crea linea, el ticket no cambia y el mensaje es claro.
7. Ticket ya vinculado o hoja cerrada: conserva las reglas actuales.

En cada caso extranjero comprueba antes y despues:

- `INDTicketInfoTable.CurrencyCode`
- `INDTicketInfoTable.TotalAmount`
- `INDTicketInfoTable.AmountMST`
- `INDTicketInfoTable.ExchRate`
- `CRMHojaGastosLine.Currency`
- `CRMHojaGastosLine.Price` y `Amount`
- `CRMHojaGastosLine.AmountMST`
- `CRMHojaGastosLine.ExchRate`
- hoja vinculada y estado del ticket

Guarda IDs, usuario, empresa, hora, operacion y `traceId` para correlacionar web, API y AX. No reutilices `F000000054` ni `F000000060` como prueba destructiva; crea tickets DEV nuevos.

### Frontend

Cubre al menos:

1. Exito total navega una sola vez.
2. Exito parcial mantiene visible el resumen y los motivos de fallo.
3. Fallo total no navega y permite reintentar tras corregir el ticket.
4. El request serializado no contiene campos monetarios.
5. El doble clic o envio repetido queda bloqueado durante la operacion.
6. El refresco posterior muestra valores devueltos por servidor sin sobrescritura optimista.

Ejecuta las comprobaciones obligatorias del repositorio, incluyendo React Doctor si hubo cambios React, el chequeo TypeScript/frontend, la compilacion de `IND_CRM_APP` y la compilacion de `IND_CRM_API` con las herramientas compatibles con su version de .NET Framework. Compila los dos objetos X++ en AX DEV y revisa errores y warnings relevantes. No publiques como parte de la validacion.

## Criterios de aceptacion

La tarea solo esta completa cuando se demuestra todo lo siguiente:

- Un ticket extranjero vinculado por web mantiene la misma moneda, importe original, importe de reembolso y tasa antes y despues.
- La linea creada refleja exactamente ese mismo snapshot.
- Un ticket vinculado mediante el boton nativo AX obtiene el mismo resultado.
- Un payload legacy sin posiciones monetarias no puede convertir silenciosamente un ticket extranjero a EUR.
- Un ticket extranjero incompleto falla antes de cualquier mutacion y queda listado como fallido en un bulk.
- EUR conserva el comportamiento existente.
- El frontend no envia importes autoritativos ni oculta fallos parciales.
- No se debilitan permisos, validaciones, reglas de propietario, estado de hoja o estado del ticket.
- No hay cambios en bundles generados, esquema AX ni dependencias.
- Los diffs se limitan a los archivos autorizados y no incluyen cambios previos de otros desarrolladores.

## Entrega esperada

Entrega al final:

1. Resumen de la causa y del diseño aplicado.
2. Lista exacta de archivos modificados por repositorio.
3. Diff funcional explicado por flujo API, AX y frontend.
4. Comandos y resultados de todas las pruebas y compilaciones.
5. Evidencia antes/despues de las pruebas AX DEV, sin datos sensibles.
6. Riesgos residuales y plan de rollback por objeto.
7. SHA inicial y SHA final de cada checkout.
8. Estado Git final y confirmacion explicita de que no hubo merge, push, PR, importacion en PROD, publicacion IIS ni despliegue de servicios.

No hagas commit ni push salvo autorizacion posterior y separada. Deja los cambios revisables en `DEV`.
