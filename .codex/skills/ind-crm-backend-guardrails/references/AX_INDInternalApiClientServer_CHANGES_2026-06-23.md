# Cambios Axapta - INDInternalApiClientServer - 2026-06-23

## Objetivo

Agregar trazas alrededor del envio `SendMailEx` para diagnosticar fallos de firma COM, transporte o configuracion sin exponer datos sensibles.

## Metodo tocado

- `INDInternalApiClientServer::sendInternalApiMailEx`

## Cambios

- Se registra una traza antes de instanciar/invocar `IND.InternalApiClient.SendMailEx`.
- La traza indica explicitamente `parameterCount=23`, alineado con el contrato vigente de `INDInternalApiClient`.
- Se registran resultado aceptado, resultado `false`, `Exception::DDEerror`, `Exception::Error` y `Exception::Internal`.
- Las trazas incluyen `company`, `sourceProcess`, `eventType`, `aggregateType`, `aggregateId`, `correlationId`, longitud de `attachmentFilePaths`, `saveToSentItems` e `importance`.
- No se registran base URL, usuario, password, client secret, direcciones de email, asunto ni cuerpo del email.

## Hallazgo relacionado

En la revision inicial, el registro COM de 32 bits resolvia:

```text
ProgID: IND.InternalApiClient
CodeBase: C:\INDAxaptaConfigAPI\API Internal Client\INDInternalApiClient.DLL
LastWriteTime: 2026-05-26 10:33:52
SHA256: CABA956559F3B728C680016D7CE376A312C88077BF2FD3B9DE523468813E699F
```

La reflexion en PowerShell de 32 bits confirmo que esa DLL registrada exponia `SendMailEx` con 22 parametros. El codigo fuente de `C:\INDProjects\IND_INTERNAL_API\INDInternalApiClient` expone 23 parametros, incluyendo `attachmentFilePaths`.

## Validacion de despliegue

El 2026-06-23 se ejecuto:

```powershell
.\scripts\deploy-indinternalapiclient-com.ps1 -Configuration Release
```

Resultado:

- Build `Release|x86` correcto, sin warnings ni errores.
- DLL copiada y registrada desde `C:\INDAxaptaConfigAPI\API Internal Client\INDInternalApiClient.dll`.
- Type library regenerada en `C:\INDAxaptaConfigAPI\API Internal Client\INDInternalApiClient.tlb`.
- PowerShell x86 confirma `SendMailEx` con 23 parametros y `attachmentFilePaths` en posicion 13.
- SHA256 de la DLL registrada: `B2760CB88E0B8DA351A518C4F93E0619991DD68FB9535638AB8CCA13503881EE`.
- Health de `https://dev.service.insertec.eu:2087/api/health/status` devuelve HTTP 200.

## Compatibilidad

- No cambia la firma de `sendInternalApiMailEx`.
- No cambia el contrato COM esperado por Axapta.
- No introduce dependencias nuevas.

## Validacion pendiente en Axapta

- Importar y compilar `INDInternalApiClientServer.xpo` en Axapta.
- Ejecutar `Job_INDInternalApi_SendMailEx` y confirmar que la traza muestra `parameterCount=23`.
- Simular fallo de email y confirmar que el metodo devuelve `false` con warning, sin exponer secretos.
