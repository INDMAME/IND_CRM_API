# Validación de cambios en IND_CRM_API

Aplicar solo las comprobaciones relacionadas con el alcance y riesgo, comenzando por la más específica.

## Siempre

- Revisar `git status`, diff completo y trabajo ajeno.
- Confirmar compatibilidad o migración aprobada.
- Confirmar que no se añadieron secretos, URLs privadas ni dependencias innecesarias.
- Revisar registros y mensajes para evitar datos sensibles.

## Documentación

- No dejar bitácoras, temporales, planes cerrados ni definiciones duplicadas.
- Validar enlaces Markdown y encabezados.
- Si cambia `.codex/*.md`: `npm run sync:codex:references` y `npm run check:codex:references`.
- Mantener `ENDPOINTS.md`, `MCP_TOOLS.json`, `MCP_ENDPOINTS.md` y Postman alineados cuando su contrato cambie.
- Validar que `MCP_TOOLS.json` sea JSON válido y que sus nombres coincidan exactamente con los encabezados `### Tool:` de `MCP_ENDPOINTS.md`; los endpoints no expuestos deben indicarlo expresamente.
- Para cada herramienta `/api/crm/*`, comprobar las cuatro cabeceras de contexto firmado en `inputSchema`, `x-http.requiredHeaders` y ejemplos, además de las cabeceras funcionales propias del endpoint.
- Si cambia el conocimiento de ayuda, comparar el bundle con la fuente APP y ejecutar sus evaluaciones en el proyecto propietario.

## API y C#

- Compilar `Release|x86` con el flujo mantenido del repositorio.
- Ejecutar las pruebas específicas existentes del módulo.
- Revisar ruta, verbo, restricciones, cabeceras, fechas, estado, envoltorio, nulabilidad y serialización.
- Confirmar que los consumidores APP siguen siendo compatibles.

## Identidad y seguridad

- Verificar contexto firmado, empresa permitida, revisión de permisos y actor funcional.
- Probar usuario normal, autogestión, responsable y subordinado cuando cambien reglas de Gastos o visibilidad.
- Comprobar que una cabecera manipulable no eleva permisos ni permite leer o modificar datos de otro usuario.

## AX/COM/XPO

- Para C# COM: sesión común, acceso serializado, propiedad y liberación de objetos, reintento acotado y registros saneados.
- Para XPO: ejecutar el comprobador de paridad/formato y seguir `AX_XPO_WORKFLOW.md`.
- Distinguir compilación C# de importación, compilación y prueba dentro de AX.

## Publicación, solo si se solicita

- API DEV: validar `Release|x86`, ejecutar `.\scripts\reinstall-api.ps1 -Apply` desde la raiz, comprobar servicio/proceso, HTTPS health, hashes y árbol publicado.
- Producción: solo por PR protegida `DEV` → `PROD` conforme a `AGENTS.md`.
- Informar cualquier paso AX aún manual; una API saludable no demuestra que los XPO estén activos.
