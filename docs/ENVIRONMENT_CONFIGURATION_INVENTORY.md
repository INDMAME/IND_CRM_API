# Environment Configuration Inventory

Fecha: 2026-03-23

## Resumen

- Solo se soportan `DEV` y `PROD`.
- `App.config` queda como fallback local.
- La configuracion operativa se gestiona con variables de entorno de maquina.
- Los secretos y valores criticos se cargan fuera del repo.

## Entornos

| Entorno | URL | Host | IP | AX config | Blob |
| --- | --- | --- | --- | --- | --- |
| `DEV` | `https://dev.insertec.biz:7776/` | `dev.insertec.biz` | `192.168.0.146` | `C:\INDAxaptaConfigAPI\CRM_API_AxConfig_DEV.axc` | `DEV` |
| `PROD` | `https://crm.insertec.biz:7776/` | `crm.insertec.biz` | `212.142.143.182` | `C:\INDAxaptaConfigAPI\CRM_API_AxConfig.axc` | `PROD` |

## Scripts

### `scripts/set-indcrm-machine-env.ps1`

Que hace:

- Crea o actualiza la configuracion base del entorno.
- No pide valores por consola.
- Salta placeholders sensibles.

Uso:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\set-indcrm-machine-env.ps1 -TargetEnvironment PROD
powershell -ExecutionPolicy Bypass -File .\scripts\set-indcrm-machine-env.ps1 -TargetEnvironment PROD -Apply
```

### `scripts/set-indcrm-machine-critical-env.ps1`

Que hace:

- Pide y guarda los valores reales y sensibles.
- Sin `-Apply` solo muestra preview.
- Con `-Apply` solicita los valores uno a uno.

Uso:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\set-indcrm-machine-critical-env.ps1 -TargetEnvironment PROD
powershell -ExecutionPolicy Bypass -File .\scripts\set-indcrm-machine-critical-env.ps1 -TargetEnvironment PROD -Apply
```

## Uso rapido

1. Cargar la base del entorno.
2. Cargar los valores criticos reales.
3. Reiniciar el servicio.
4. Validar `ping`, `health` y `getEnvironmentName`.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\set-indcrm-machine-env.ps1 -TargetEnvironment PROD -Apply
powershell -ExecutionPolicy Bypass -File .\scripts\set-indcrm-machine-critical-env.ps1 -TargetEnvironment PROD -Apply
Restart-Service IND_CRM_API
```

## Que esperar en consola

- Script base: no pregunta nada; solo crea variables y salta placeholders.
- Script critico sin `-Apply`: solo muestra preview.
- Script critico con `-Apply`: pide los valores uno a uno.

## Variables necesarias

### Base por ambiente

- `IND_ENV`
- `INDCRM_AX_CONFIG_FILE`
- `INDCRM_AX_VERBOSE_LOGGING`
- `INDCRM_AX_VERBOSE_LOG_PATH`
- `INDCRM_AX_ALLOW_DEFAULT_CREDENTIALS`
- `INDCRM_BASE_URL`
- `INDCRM_PUBLIC_HOST`
- `INDCRM_PUBLIC_IP`
- `INDCRM_PUBLIC_PORT`
- `INDCRM_CORS_ENABLED`
- `INDCRM_CORS_ALLOWED_ORIGINS`
- `INDCRM_LOG_LEVEL`
- `INDCRM_LOG_PATH`
- `INDCRM_JWT_ISSUER`
- `INDCRM_JWT_AUDIENCE`
- `INDCRM_JWT_EXPIRATION_MINUTES`
- `INDCRM_JWT_REFRESH_THRESHOLD_MINUTES`
- `OPENAI_TRANSCRIPTION_DEFAULT_PROMPT_PATH`
- `AZURE_BLOB_CONTAINER`
- `AZURE_BLOB_ENVIRONMENT_SEGMENT`
- `COMPANY_ACCESS_CACHE_MINUTES`

### Criticas

- `USER_DEFAULT`
- `USER_PASS_DEFAULT`
- `JWT_SECRET_KEY`
- `OPENAI_API_KEY`
- `AZURE_BLOB_CONNECTION_STRING`
- `AZURE_DOCS_IA_KEY`
- `AZURE_DOCS_IA_ENDPOINT`
- `AZURE_DOCS_IA_MODEL`

## Scripts locales no versionados

Para scripts privados de maquina o utilidades con datos sensibles, usar:

- `scripts/local/`
- `scripts/*.local.ps1`
- `scripts/*.secrets.ps1`

Esas rutas quedan ignoradas por Git.

## Notas

- `INDCRM_PUBLIC_HOST`, `INDCRM_PUBLIC_IP` y `INDCRM_PUBLIC_PORT` son datos operativos para DNS, firewall y despliegue.
- La IP de `DEV` queda confirmada en `192.168.0.146`.
- La web `DEV` se sirve en `https://dev.insertec.biz:7702/`; la API `DEV` mantiene `https://dev.insertec.biz:7776/`.
- El cambio de `DEV` a `PROD` debe resolverse en despliegue, no en recompilacion.
