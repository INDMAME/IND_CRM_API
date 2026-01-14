# IND Ax Session Stability

METODOLOGIA (Spec-Driven Development) - Documento base

## A) ANALISIS

### Componentes de sesion y login AX
- Core de sesiones: `Services/AxaptaSessionManager.cs`
- Interface: `Services/Interfaces/IAxaptaSessionManager.cs`
- Singleton global: `Services/AxSession.cs`
- Registro DI y handlers: `App_Start/DependencyConfig.cs`
- Uso de COM BC: `IND_CRM_API.csproj` (COMReference: AxaptaCOMConnector)
- Controladores con AX: `Controllers/System/AuthController.cs`, `Controllers/CRM/*`, `Controllers/System/EnvironmentController.cs`
- Helpers de contenedores: `Helpers/AxContainerHelper.cs`, `Helpers/ContainerDebugHelper.cs`
- Configuracion: `App.config` (AxConfigFile, Axapta.User/Password, etc.)

### Modelo de ciclo de vida actual
- `AxaptaSessionManager` es singleton.
- Mantiene sesiones Axapta2Class por usuario en memoria (_sessionsByUser).
- Reusa la misma instancia COM entre requests del mismo usuario.
- Limpieza por expiracion con tarea de fondo.
- Passwords se guardan en memoria (_passwordByUser).

### Riesgos actuales
- Concurrencia: Axapta2Class compartida entre requests y threads sin proteccion.
- COM thread-safety: llamadas con Task.Run a la misma instancia COM.
- Cleanup vs in-flight: limpieza puede cerrar sesion activa.
- Refresh inconsistente: RefreshSessionToken no valida salud real de la sesion COM.
- Password rotation: mismatch bloquea re-login (SESSION-REFRESH-DENIED).
- Reinicios: tokens JWT siguen validos pero no hay password en memoria.

### Log de SESSION-REFRESH-DENIED
Se genera en `AxaptaSessionManager.CreateOrGetSession(...)` cuando:
- Existe sesion previa en _sessionsByUser
- Se envia password nuevo y no coincide con _passwordByUser[username]
Resultado: se loguea y retorna false.

## B) ESPECIFICACION

### Requisitos funcionales (RF)
- RF1: Verificar conectividad BC al inicio y por request (smoke check opcional).
- RF2: Renovar sesion BC cuando expire o falle por invalid session, sin reutilizar sesion corrupta.
- RF3: Si cambia password (rotacion), detectar mismatch y forzar re-logon limpio.
- RF4: No tumbar el servicio por excepciones COM; devolver error controlado y log detallado.

### Requisitos no funcionales (RNF)
- RNF1: Concurrencia segura: no compartir una misma instancia COM entre threads sin proteccion.
- RNF2: Trazabilidad: correlationId, axUser, company, endpoint, duracion, reintentos, motivo de refresh, callStackText cuando exista.

## C) DISENO (Opcion 1: sesion por request)

### Enfoque
- Cada request obtiene su propia instancia Axapta2Class.
- La instancia se crea on-demand y se libera al terminar el request.
- No se comparten instancias COM entre requests.

### Componentes nuevos
- `IND_AxSessionGuard`
  - EnsureLoggedOn()
  - ExecuteWithRetryOnSessionErrors()
  - SafeLogoffAndDispose()
  - SmokeTest()
- `IND_AxRequestContext`
  - Guarda correlationId, endpoint, company, usuario, y Axapta2Class del request.
- `IND_AxSessionScopeHandler`
  - Inicializa el contexto por request y garantiza logoff/Dispose al final.

### Retry
- Maximo 1 reintento ante errores tipicos de sesion (invalid session, logon required).

### Encapsulamiento
- No se cambian controllers; el comportamiento se concentra en el manager/guard/handler.

## D) TAREAS
1) Crear documento de especificacion y diseno (este archivo).
2) Agregar clases `IND_AxSessionGuard`, `IND_AxRequestContext`.
3) Agregar handler `IND_AxSessionScopeHandler` y registrarlo en `DependencyConfig`.
4) Refactor de `AxaptaSessionManager` para sesion por request, password rotation y retry.
5) Actualizar `IND_CRM_API.csproj` con los nuevos archivos.
6) Documentar verificacion manual y observacion de logs.
