# Reglas generales de IND_CRM_API

## Alcance y precedencia

Estas reglas cubren todo el repositorio. Los contratos y detalles están separados por temática en `.codex/README.md` y no deben duplicarse aquí.

Cuando dos reglas locales no coincidan, revisar el código, las rutas y el flujo realmente ejecutado. Mantener ese comportamiento salvo requisito explícito o defecto demostrado.

## Principios de trabajo

- Antes de un cambio importante, presentar un plan breve con alcance, propietarios, contratos, riesgos y validación.
- Preferir cambios pequeños, compatibles y reversibles; no hacer migraciones globales o refactors cruzados sin petición.
- Revisar estado de Git, diff y archivos no rastreados. No sobrescribir trabajo ajeno.
- Mantener límites claros entre controlador, DTO, validación, servicio, mapper y acceso AX.
- Reutilizar una abstracción común solo cuando exista reutilización real, no especulativa.
- No añadir dependencias sin necesidad clara y justificación breve.
- No ocultar errores, debilitar pruebas ni inventar evidencia del entorno de ejecución.

## Restricciones no negociables

- .NET Framework 4.8, Web API 2, OWIN self-host y plataforma x86 por el Business Connector de Axapta 3.0.
- No migrar de framework, arquitectura de host o conector COM sin una petición específica y un plan de compatibilidad.
- Las llamadas AX pasan por la infraestructura común descrita en `TECH_SPECS.md`; no se crean sesiones COM ad hoc ni concurrencia paralela contra COM.
- Los endpoints publicados mantienen ruta y contrato salvo cambio aprobado y documentado.
- `ENDPOINTS.md` es la fuente HTTP; `MCP_TOOLS.json`, la de schemas MCP; `.codex/Axapta`, la fuente canónica XPO compartida con APP.

## Seguridad y configuración

- Nunca versionar secretos, contraseñas, tokens, claves, cadenas de conexión, identificadores privados ni valores operativos sensibles.
- Mantener los mismos nombres y orden de resolución de configuración en DEV y PROD; solo cambia el valor externo.
- No confiar en identidad, empresa, propietario o permisos suministrados por el cliente cuando existe contexto firmado del servidor.
- Los registros no incluyen secretos, tokens completos ni contenido sensible innecesario.

## Idioma y documentación

- La documentación del proyecto se escribe en español.
- Por la instrucción superior activa, los comentarios nuevos de código y mensajes de commit usan inglés simple y ASCII. No reformatear comentarios históricos fuera del bloque tocado.
- No crear bitácoras Markdown, prompts temporales o informes fechados. Actualizar el documento temático vigente.
- Las reglas comunes de AX/XPO viven solo en `AX_XPO_WORKFLOW.md` y deben ser idénticas en APP y API.

## Git, publicación y producción

- El trabajo normal permanece en `DEV`. Commit, push y despliegue requieren petición explícita.
- Publicar la API en DEV significa usar el flujo mantenido `.\scripts\reinstall-api.ps1 -Apply` desde la raiz y verificar proceso/servicio, HTTPS y artefactos, solo cuando el usuario lo solicite.
- Solo una petición explícita de promoción a producción autoriza un PR `DEV` → `PROD` numerado `Release <N>`, con comprobaciones y auto-merge.
- Nunca sustituir un PR bloqueado por merge o push directo a `PROD`/`main`.
- Una publicación API o web no importa, compila, sincroniza ni activa XPO en Axapta.

## Cierre

- Ejecutar la validación proporcional de `QUALITY_CHECKLIST.md`.
- Revisar routing y documentación cuando cambie un contrato.
- Informar resultados reales, impacto, supuestos y cualquier validación manual pendiente.
