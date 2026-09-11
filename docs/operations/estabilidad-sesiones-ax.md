# Estabilidad de sesiones de Axapta

## Objetivo

Este documento describe el comportamiento vigente de las sesiones de Axapta 3.0
en `IND_CRM_API`. La implementación real en código tiene prioridad si este texto
queda desactualizado.

## Arquitectura actual

- `AxaptaSessionManager` coordina credenciales, tokens y el ciclo de vida de la
  sesión COM.
- `IND_AxSessionScopeHandler` abre un contexto por cada petición HTTP y garantiza
  su cierre en un bloque `finally`.
- `IND_AxRequestContext` conserva en `AsyncLocal` el `correlationId`, el `traceId`,
  el endpoint, la empresa y la sesión COM de la petición actual.
- `IND_AxSessionGuard` centraliza el acceso a Business Connector, el inicio de
  sesión, el tiempo de espera, el reintento controlado y la liberación de recursos.
- `AxaptaComSession` encapsula la instancia `Axapta2Class` y registra los objetos
  AX/COM creados durante la operación.

La sesión COM pertenece a una sola petición. Puede reutilizarse dentro de esa
misma petición si usuario, configuración y empresa coinciden, pero se cierra al
terminar la petición. No se comparte una instancia COM entre peticiones.

## Flujo por petición

1. `IND_AxSessionScopeHandler` obtiene los identificadores de diagnóstico y abre
   el contexto mediante `BeginRequestScope`.
2. El primer acceso a AX llama a `GetAxInstanceForUser`.
3. El gestor resuelve las credenciales disponibles y solicita al guard una
   instancia autenticada de `Axapta2Class`.
4. Las llamadas AX pasan por `AxaptaComSession` y `IND_AxSessionGuard`.
5. Al finalizar, `EndRequestScope` libera los objetos registrados, ejecuta
   `Logoff`, libera la sesión y limpia el contexto.

## Concurrencia y tiempo de espera

- El acceso COM puede serializarse para todo el proceso mediante
  `AxaptaComOptions.SerializeComAccess`.
- No deben agregarse `Task.Run`, `Parallel.ForEach` ni llamadas COM paralelas en
  controladores o servicios.
- `IND_AxSessionGuard.ExecuteComCall` usa internamente un hilo controlado para
  detectar bloqueos y aplicar el tiempo de espera configurado. Esta es una excepción
  encapsulada del envoltorio y no autoriza concurrencia adicional.
- Tras un tiempo de espera agotado se aplica un periodo corto de indisponibilidad para evitar una
  cascada de llamadas contra una sesión bloqueada.

## Recuperación y reintentos

- Solo se permite un reintento cuando el error se reconoce como recuperable.
- Antes del reintento se descarta la sesión de la petición para crear una nueva.
- El error COM `0x80041004` puede indicar contaminación del proceso Business
  Connector y activa la recuperación controlada configurada.
- Un reinicio de COM+ es una medida de recuperación protegida por configuración,
  bloqueo y trazas; no forma parte del flujo normal de cada petición.

## Seguridad y trazabilidad

- Las credenciales no deben aparecer en registros ni documentos.
- Las trazas incluyen `correlationId`, `traceId`, endpoint, empresa, usuario AX,
  duración, reintentos y etapa de la operación cuando corresponde.
- Los flujos del asistente de ayuda ocultan empresa y usuario en las trazas del
  proceso para reducir la exposición de contexto.
- Los objetos AX/COM no deben conservarse en campos estáticos mutables ni fuera
  del alcance de la petición.

## Validación operativa

Para validar un cambio en esta capa:

1. Compilar `IND_CRM_API.sln` en `Release|x86`.
2. Comprobar `GET /api/health/ping`.
3. Ejecutar una operación CRM de lectura y otra de escritura autorizada.
4. Confirmar en los registros el ciclo `begin-request-scope` / `end-request-scope`.
5. Verificar que no quedan sesiones u objetos COM vivos tras la respuesta.
6. Probar de forma controlada el error o tiempo de espera afectado cuando el cambio trate
   la recuperación.

La compilación y las pruebas de API no demuestran el estado del AOT. Los cambios
XPO requieren importación, compilación, sincronización cuando aplique y prueba
funcional independiente en Axapta.
