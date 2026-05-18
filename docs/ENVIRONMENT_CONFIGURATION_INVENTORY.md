# Environment Configuration Inventory

Fecha: 2026-04-07

## Resumen

- Solo se soportan `DEV` y `PROD`.
- `App.config` queda como fallback local.
- La configuracion operativa se gestiona con variables de entorno de maquina.
- Todas las claves consumidas por la API pueden resolverse desde variables de entorno de maquina.
- Los secretos y valores criticos se cargan fuera del repo.
- Los scripts de bootstrap exigen `TargetEnvironment` explicito para evitar defaults peligrosos.
- Los scripts de bootstrap ya incluyen claves de servicio y HTTPS usadas por los `.bat`.
- Los scripts de bootstrap gestionan tambien las claves usadas por `IND_CRM_APP`, incluyendo endpoint publico web y Entra/OIDC.
- `INDCRM_BASE_URL` y `INDCRM_PUBLIC_*` describen la API; `INDCRM_WEB_BASE_URL` e `INDCRM_WEB_PUBLIC_*` describen la web.
- Si `IND_ENV` es `DEV` o `PROD`, la API exige `INDCRM_BASE_URL` con `https://`, exige `INDCRM_PUBLIC_HOST`/`INDCRM_PUBLIC_PORT`, valida que coincidan y no arranca con fallback local.
- Los `.bat` versionados no deben guardar passwords, usuarios personales ni secretos de certificados.

## Entornos

| Entorno | URL | Host | IP | AX config | Blob |
| --- | --- | --- | --- | --- | --- |
| `DEV` | `https://dev.insertec.biz:2083/` | `dev.insertec.biz` | `192.168.0.148` | `C:\INDAxaptaConfigAPI\CRM_API_AxConfig_DEV.axc` | `DEV` |
| `PROD` | `https://crm.insertec.biz:7776/` | `crm.insertec.biz` | `212.142.143.182` | `C:\INDAxaptaConfigAPI\CRM_API_AxConfig_PROD.axc` | `PROD` |

## Endpoints web

| Entorno | WEB URL | WEB host | WEB port | API URL |
| --- | --- | --- | --- | --- |
| `DEV` | `https://dev.insertec.biz:2053/` | `dev.insertec.biz` | `2053` | `https://dev.insertec.biz:2083/` |
| `PROD` | `https://crm.insertec.biz:7702/` | `crm.insertec.biz` | `7702` | `https://crm.insertec.biz:7776/` |

## Scripts

### `scripts/set-indcrm-machine-env.ps1`

Que hace:

- Crea o actualiza la configuracion base del entorno.
- No pide valores por consola.
- Salta placeholders sensibles.
- Escribe tambien `ASPNETCORE_ENVIRONMENT` por entorno: `Development` para `DEV` y `Production` para `PROD`.
- Escribe las claves web no sensibles: `INDCRM_WEB_BASE_URL`, `INDCRM_WEB_PUBLIC_HOST`, `INDCRM_WEB_PUBLIC_PORT`, `IND_E2E_BASE_URL` y `ApiSettings__BaseUrl`.
- Muestra placeholders para las claves Entra/OIDC de la web y las salta hasta que se carguen con el script interactivo.
- Configura tambien `INDCRM_SERVICE_USER` e `INDCRM_HTTP_SERVICE_USER`.
- Incluye tambien defaults operativos de OpenAI, Azure Docs IA y tipos de cambio.
- Requiere indicar `-TargetEnvironment` de forma explicita.

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
- Incluye `INDCRM_SERVICE_PASSWORD` y la password PFX del entorno objetivo.
- Incluye las claves Entra/OIDC usadas por `IND_CRM_APP`.
- Incluye `INDCRM_CONTEXT_TOKEN_SECRET_KEY` y la ruta PFX opcional del entorno objetivo.
- Requiere indicar `-TargetEnvironment` de forma explicita.

Uso:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\set-indcrm-machine-critical-env.ps1 -TargetEnvironment PROD
powershell -ExecutionPolicy Bypass -File .\scripts\set-indcrm-machine-critical-env.ps1 -TargetEnvironment PROD -Apply
```

### `scripts/set-indcrm-machine-all-env.ps1`

Que hace:

- Pide y actualiza todas las variables de entorno operativas en un solo flujo.
- Usa defaults por entorno para URL, host, IP, AX config, blob y otras claves base.
- Incluye `ASPNETCORE_ENVIRONMENT` para que `IND_CRM_APP` no dependa del valor por defecto del host.
- Incluye los defaults web por ambiente y evita que `ApiSettings__BaseUrl` quede desalineada respecto a `INDCRM_BASE_URL`.
- Incluye las claves Entra/OIDC requeridas por la web.
- Mantiene la logica interactiva de `Enter` para conservar el valor actual o usar el default.
- Permite dejar vacias claves opcionales como `INDCRM_CORS_ALLOWED_ORIGINS` y `OPENAI_TRANSCRIPTION_DEFAULT_PROMPT`.
- Para vaciar una clave opcional existente, admite escribir `__CLEAR__`.
- Incluye tambien las claves de servicio y la password PFX del entorno objetivo.
- Incluye la ruta PFX default del entorno objetivo.
- Requiere indicar `-TargetEnvironment` de forma explicita.

Uso:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\set-indcrm-machine-all-env.ps1 -TargetEnvironment PROD
powershell -ExecutionPolicy Bypass -File .\scripts\set-indcrm-machine-all-env.ps1 -TargetEnvironment PROD -Apply
```

### `bin\x86\Release\enable_https_2083_dev.bat`

Que hace:

- Configura HTTPS de `HTTP.sys` para `https://dev.insertec.biz:2083/`.
- Exige una maquina ya configurada como `DEV` con `IND_ENV`, `INDCRM_BASE_URL`, `INDCRM_PUBLIC_HOST`, `INDCRM_PUBLIC_PORT` e `INDCRM_HTTP_SERVICE_USER` coherentes.
- Usa por defecto el PFX operativo `C:\INDAxaptaConfigAPI\dev.insertec.biz\dominio.pfx`.
- Permite override de ruta por `INDCRM_DEV_PFX_PATH` o por el primer argumento.
- Si no recibe password por argumento ni existe `INDCRM_DEV_PFX_PASSWORD`, pide la password por consola de forma segura.
- Importa el certificado en `Cert:\LocalMachine\My`, valida que el certificado corresponde al host `dev.insertec.biz`, guarda backup del `sslcert` actual en `%TEMP%` y reaplica `urlacl` + `sslcert`.

Uso:

```bat
cmd /c .\bin\x86\Release\enable_https_2083_dev.bat
cmd /c .\bin\x86\Release\enable_https_2083_dev.bat "C:\otra\ruta\dominio.pfx"
```

Nota:

- Evitar pasar la password como segundo argumento salvo automatizacion controlada.
- El script no descarga el PFX: espera un archivo local ya provisionado.
- `bin\x86\Release\enable_https_17776_dev.bat` y `bin\x86\Release\enable_https_7776_dev.bat` quedan como wrappers de compatibilidad y redirigen al script de `2083`.

### `bin\x86\Release\enable_https_7776.bat`

Que hace:

- Configura HTTPS de `HTTP.sys` para `https://crm.insertec.biz:7776/`.
- Exige una maquina ya configurada como `PROD` con `IND_ENV`, `INDCRM_BASE_URL`, `INDCRM_PUBLIC_HOST`, `INDCRM_PUBLIC_PORT` e `INDCRM_HTTP_SERVICE_USER` coherentes.
- Usa por defecto `certificados\dominio.pfx` dentro del arbol local del repo.
- Permite override de ruta por `INDCRM_PROD_PFX_PATH` o por el primer argumento.
- Si no recibe password por argumento ni existe `INDCRM_PROD_PFX_PASSWORD`, pide la password por consola de forma segura.
- Importa el certificado en `Cert:\LocalMachine\My`, valida que el certificado corresponde al host `crm.insertec.biz`, guarda backup del `sslcert` actual en `%TEMP%` y reaplica `urlacl` + `sslcert`.

Uso:

```bat
cmd /c .\bin\x86\Release\enable_https_7776.bat
cmd /c .\bin\x86\Release\enable_https_7776.bat "C:\otra\ruta\dominio.pfx"
```

Nota:

- El script no descarga el PFX. Ademas, `certificados/` esta ignorado por Git, asi que ese secreto no viaja al capturar cambios.

### `bin\x86\Release\instalar_api_axapta.bat`

Que hace:

- Instala el servicio Windows `IND_CRM_API` usando las credenciales de servicio de maquina.
- Exige `IND_ENV`, `INDCRM_PUBLIC_HOST` e `INDCRM_PUBLIC_PORT`; no asume `localhost:7776`.
- Muestra el endpoint local/publico solo despues de resolver las claves de maquina.
- La fuente versionada esta en `Bats`; MSBuild la copia a `bin\x86\Release` al compilar.

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

Alternativa en un solo paso:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\set-indcrm-machine-all-env.ps1 -TargetEnvironment PROD -Apply
Restart-Service IND_CRM_API
```

## Que esperar en consola

- Script base: no pregunta nada; solo crea variables y salta placeholders.
- Script critico sin `-Apply`: solo muestra preview.
- Script critico con `-Apply`: pide los valores uno a uno.
- Script all-env sin `-Apply`: muestra valor actual y default del entorno objetivo.
- Script all-env con `-Apply`: pide todas las variables en un solo recorrido.

## Variables necesarias

### Base por ambiente

- `IND_ENV`
- `ASPNETCORE_ENVIRONMENT`
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
- `ApiSettings__BaseUrl`
- `INDCRM_WEB_BASE_URL`
- `INDCRM_WEB_PUBLIC_HOST`
- `INDCRM_WEB_PUBLIC_PORT`
- `IND_E2E_BASE_URL`
- `INDCRM_SERVICE_USER`
- `INDCRM_HTTP_SERVICE_USER`
- `INDCRM_JWT_ISSUER`
- `INDCRM_JWT_AUDIENCE`
- `INDCRM_JWT_EXPIRATION_MINUTES`
- `INDCRM_JWT_REFRESH_THRESHOLD_MINUTES`
- `OPENAI_TRANSCRIPTION_DEFAULT_PROMPT_PATH`
- `OPENAI_TRANSCRIPTION_DEFAULT_PROMPT`
- `OPENAI_AUDIO_MODEL`
- `OPENAI_TIMEOUT_SECONDS`
- `OPENAI_MODERATION_MODEL`
- `OPENAI_TRANSCRIPTION_PROMPT_MAX_WORDS`
- `OPENAI_EXPENSE_TICKET_MODEL`
- `OPENAI_EXPENSE_TICKET_TIMEOUT_SECONDS`
- `OPENAI_EXPENSE_TICKET_MAX_IMAGE_BYTES`
- `OPENAI_EXPENSE_TICKET_MAX_OUTPUT_TOKENS`
- `OPENAI_EXPENSE_TICKET_IMAGE_DETAIL`
- `OPENAI_EXPENSE_TICKET_SERVICE_TIER`
- `OPENAI_EXPENSE_TICKET_PROFILE_TAG`
- `OPENAI_EXPENSE_TICKET_PROMPT_CACHE_KEY`
- `OPENAI_EXPENSE_TICKET_REASONING_EFFORT`
- `OPENAI_EXPENSE_TICKET_QUICK_CREATE_MAX_OUTPUT_TOKENS`
- `OPENAI_EXPENSE_TICKET_QUICK_CREATE_IMAGE_DETAIL`
- `OPENAI_EXPENSE_TICKET_QUICK_CREATE_SERVICE_TIER`
- `OPENAI_EXPENSE_TICKET_QUICK_CREATE_PROFILE_TAG`
- `OPENAI_EXPENSE_TICKET_QUICK_CREATE_PROMPT_CACHE_KEY`
- `OPENAI_EXPENSE_TICKET_QUICK_CREATE_REASONING_EFFORT`
- `OPENAI_EXPENSE_SHEET_ASK_MODEL`
- `OPENAI_EXPENSE_SHEET_ASK_TIMEOUT_SECONDS`
- `OPENAI_EXPENSE_SHEET_ASK_MAX_OUTPUT_TOKENS`
- `OPENAI_EXPENSE_SHEET_ASK_CHUNK_MAX_OUTPUT_TOKENS`
- `OPENAI_EXPENSE_SHEET_ASK_DIRECT_RECORD_LIMIT`
- `OPENAI_EXPENSE_SHEET_ASK_CHUNK_SIZE`
- `OPENAI_EXPENSE_SHEET_ASK_MAX_CHUNKS`
- `OPENAI_EXPENSE_SHEET_ASK_SERVICE_TIER`
- `OPENAI_EXPENSE_SHEET_ASK_PROFILE_TAG`
- `OPENAI_EXPENSE_SHEET_ASK_PROMPT_CACHE_KEY`
- `OPENAI_EXPENSE_SHEET_ASK_REASONING_EFFORT`
- `OPENAI_RATE_LIMIT_ENABLED`
- `OPENAI_RATE_LIMIT_SPEECH_MAX_REQUESTS`
- `OPENAI_RATE_LIMIT_SPEECH_WINDOW_SECONDS`
- `OPENAI_RATE_LIMIT_EXPENSE_TICKET_MAX_REQUESTS`
- `OPENAI_RATE_LIMIT_EXPENSE_TICKET_WINDOW_SECONDS`
- `OPENAI_RATE_LIMIT_MAX_CONCURRENT_PER_USER`
- `OPENAI_RATE_LIMIT_VALIDATION_MULTIPLIER`
- `AZURE_BLOB_CONTAINER`
- `AZURE_BLOB_ENVIRONMENT_SEGMENT`
- `AZURE_DOCS_IA_API_VERSION`
- `AZURE_DOCS_IA_POLL_INTERVAL_MS`
- `AZURE_DOCS_IA_TIMEOUT_SECONDS`
- `AZURE_DOCS_IA_BLOB_READ_SAS_MINUTES`
- `EXCHANGE_RATE_ECB_TIMEOUT_SECONDS`
- `EXCHANGE_RATE_FRANKFURTER_TIMEOUT_SECONDS`
- `EXCHANGE_RATE_OPEN_ER_API_TIMEOUT_SECONDS`
- `AXAPTA_CALL_TIMEOUT_SECONDS`
- `CLIENT_SETTINGS_PROVIDER_SERVICE_URI`
- `COMPANY_ACCESS_CACHE_MINUTES`

### Web y Entra/OIDC

- `CRM_TENANT_ID`
- `CRM_CLIENT_ID`
- `CRM_CLIENT_SECRET`
- `CRM_AUTHORITY`

### Criticas

- `USER_DEFAULT`
- `USER_PASS_DEFAULT`
- `CRM_TENANT_ID`
- `CRM_CLIENT_ID`
- `CRM_CLIENT_SECRET`
- `CRM_AUTHORITY`
- `INDCRM_SERVICE_PASSWORD`
- `JWT_SECRET_KEY`
- `INDCRM_CONTEXT_TOKEN_SECRET_KEY`
- `OPENAI_API_KEY`
- `AZURE_BLOB_CONNECTION_STRING`
- `AZURE_DOCS_IA_KEY`
- `AZURE_DOCS_IA_ENDPOINT`
- `AZURE_DOCS_IA_MODEL`

### Defaults operativos actualizados

- `USER_DEFAULT`: `APIAX`
- `AZURE_DOCS_IA_ENDPOINT`: `https://westeurope.api.cognitive.microsoft.com/`
- `OPENAI_EXPENSE_SHEET_ASK_MODEL`: `gpt-5.4-mini`
- `OPENAI_EXPENSE_SHEET_ASK_REASONING_EFFORT`: `low`
- `OPENAI_EXPENSE_TICKET_MODEL`: `gpt-5.4-nano`
- `INDCRM_CONTEXT_TOKEN_AUDIENCE`: `IND_CRM_WEB_CONTEXT`
- `INDCRM_CONTEXT_TOKEN_ISSUER`: `IND_CRM_CONTEXT`
- `INDCRM_CONTEXT_TOKEN_SECRET_KEY`: generado por el script al aplicar si la maquina no tiene valor previo; debe quedar distinto entre DEV y PROD.
- `ASPNETCORE_ENVIRONMENT`: `DEV` -> `Development`, `PROD` -> `Production`.
- `INDCRM_WEB_BASE_URL`: `DEV` -> `https://dev.insertec.biz:2053/`, `PROD` -> `https://crm.insertec.biz:7702/`
- `INDCRM_WEB_PUBLIC_HOST`: `DEV` -> `dev.insertec.biz`, `PROD` -> `crm.insertec.biz`
- `INDCRM_WEB_PUBLIC_PORT`: `DEV` -> `2053`, `PROD` -> `7702`
- `ApiSettings__BaseUrl`: mismo valor que `INDCRM_BASE_URL` para evitar overrides antiguos de la web.

### HTTPS opcionales por ambiente

- `INDCRM_DEV_PFX_PATH`
- `INDCRM_DEV_PFX_PASSWORD`
- `INDCRM_PROD_PFX_PATH`
- `INDCRM_PROD_PFX_PASSWORD`

Defaults:

- `INDCRM_DEV_PFX_PATH`: `C:\INDAxaptaConfigAPI\dev.insertec.biz\dominio.pfx`
- `INDCRM_PROD_PFX_PATH`: `C:\INDAxaptaConfigAPI\crm.insertec.biz\dominio.pfx`

## Scripts locales no versionados

Para scripts privados de maquina o utilidades con datos sensibles, usar:

- `scripts/local/`
- `scripts/*.local.ps1`
- `scripts/*.secrets.ps1`

Esas rutas quedan ignoradas por Git.

## Notas

- `INDCRM_PUBLIC_HOST`, `INDCRM_PUBLIC_IP` y `INDCRM_PUBLIC_PORT` son datos operativos para DNS, firewall y despliegue.
- La IP de `DEV` queda confirmada en `192.168.0.148`.
- La web `DEV` se sirve en `https://dev.insertec.biz:2053/`; la API `DEV` se sirve en `https://dev.insertec.biz:2083/`.
- La web `PROD` se sirve en `https://crm.insertec.biz:7702/`; la API `PROD` se sirve en `https://crm.insertec.biz:7776/`.
- El puerto web se aplica realmente en IIS, pero `INDCRM_WEB_BASE_URL`, `INDCRM_WEB_PUBLIC_HOST` e `INDCRM_WEB_PUBLIC_PORT` quedan como contrato de maquina y son validados por `IND_CRM_APP\publish.ps1`.
- El cambio de `DEV` a `PROD` debe resolverse en despliegue, no en recompilacion.
- `DEV` debe mantener `ASPNETCORE_ENVIRONMENT=Development`, `INDCRM_AX_CONFIG_FILE=...\CRM_API_AxConfig_DEV.axc` y `AZURE_BLOB_ENVIRONMENT_SEGMENT=DEV`; los scripts de instalacion bloquean combinaciones cruzadas con `PROD`.
- Cuando se cambie una variable de maquina usada por la API, reiniciar `IND_CRM_API` para recargar la configuracion del proceso.
- Blob usa `AZURE_BLOB_ENVIRONMENT_SEGMENT` y, si falta, hereda `IND_ENV` antes de usar un fallback neutro.
- Un `git push` entre ramas no cambia el ambiente por si solo; el riesgo real es ejecutar scripts locales con variables equivocadas.
- Los scripts versionados en `Bats` se copian a `bin\x86\Release` al compilar. `instalar_api_axapta.bat`, `enable_https_2083_dev.bat`, `enable_https_17776_dev.bat`, `enable_https_7776_dev.bat` y `enable_https_7776.bat` reciben sus claves desde las variables de maquina provisionadas por los `.ps1`.
