# Arquitectura técnica de IND_CRM_API

## Plataforma

- .NET Framework 4.8, Web API 2 y OWIN self-host.
- Compilación y ejecución x86 obligatorias por `AxaptaCOMConnector` y Axapta 3.0 SP2.
- C# 7.3. No introducir APIs o sintaxis incompatibles con esta base.

## Contratos HTTP

- `ENDPOINTS.md` define ruta, verbo, cabeceras, fechas y respuesta de cada endpoint.
- Respuestas comunes: `IndApiResponse<T>`, `IndPagedResponse<T>`, `IndValidationError` e `IndErrorCodes`.
- Al tocar un endpoint, revisar colisiones entre rutas literales o parametrizadas, unicidad de verbo y ruta, restricciones, `RoutePrefix`, rutas hermanas y enrutamiento heredado.
- Un cambio contractual actualiza DTO, validación, XML docs, atributos Swagger/OpenAPI, catálogo HTTP, MCP/Postman y consumidores afectados.
- En tickets y hojas de gastos, las peticiones admiten `DDMMYYYY` y `DD.MM.YYYY`; las respuestas públicas normalizan a `DD.MM.YYYY`.

## Identidad y contexto funcional

- El bearer autentica el canal técnico entre APP y API; `APIAX` sigue siendo la identidad técnica de la integración COM.
- La autorización funcional se aísla por `tenantId + entraOid` mediante `UserCompanyAccessCache` y un token de contexto firmado por `UserContextTokenService`.
- Los endpoints CRM dependientes de empresa validan `X-IND-Company`, OID Entra, versión de contexto, revisión de permisos y token firmado en `BaseCrmController`.
- La instantánea validada aporta el usuario AX funcional del solicitante. Una cabecera `X-IND-AxUserId` puede seguir existiendo en contratos heredados o representar al propietario de una operación concreta, pero no sustituye al actor firmado.
- En el detalle de hojas, el viewer se obtiene con `RequireValidatedSnapshotAxUserIdOrReturn403`; no se confía en el usuario enviado por el navegador para decidir qué puede ver.
- Errores de contexto: `AUTH_CONTEXT_REQUIRED`, `AUTH_CONTEXT_STALE` y `AUTH_FORBIDDEN`. La APP puede refrescar y reintentar una vez los dos primeros; no convierte un 403 real en permiso.

La explicación vigente de este flujo está en `docs/architecture/security/authentication-and-company-context.md`.

## Integración Axapta COM

- Toda llamada pasa por `IAxaptaSessionManager`/`AxaptaSessionManager` y la protección común `IND_AxSessionGuard`; no abrir sesiones directamente en controladores.
- `IND_AxSessionGuard` serializa el acceso COM con un semáforo, controla logon/logoff, objetos poseídos, liberación y un único reintento de errores de sesión previstos.
- No usar `Task.Run`, `Parallel.ForEach` ni compartir objetos `Axapta`, `AxaptaRecord`, `AxaptaObject` o `AxaptaContainer` fuera de su operación.
- Liberar únicamente objetos COM creados y poseídos por el ámbito, en orden inverso y dentro de `finally`/`Dispose` según la abstracción vigente.
- No usar `GC.Collect()` como patrón de petición.
- La recuperación COM+ es excepcional, configurada, serializada y registrada; nunca se reinicia por defecto en cada petición.
- Mantener llamadas HTTP/DLL/correo fuera de transacciones AX cuando no formen parte del commit de negocio.

## AX y XPO

- `IND_CRM_API/.codex/Axapta` es la fuente canónica; APP es espejo.
- Formato, análisis, compatibilidad y activación manual se rigen exclusivamente por `AX_XPO_WORKFLOW.md`.
- Los cambios en XPO no se consideran activos hasta importar, compilar, sincronizar si corresponde y probar en Axapta.

## Configuración y proveedores

- Reutilizar las abstracciones y claves actuales. DEV y PROD comparten contrato de configuración.
- No fijar URLs operativas, secretos ni credenciales dentro del código o la documentación contractual.
- Los hosts públicos de entorno, cuando son necesarios para operar, se documentan una sola vez bajo `docs/operations/`.
- Los proveedores externos deben tener un tiempo de espera, tratamiento explícito de errores y registros saneados, y no deben bloquear una transacción AX ya confirmada.
