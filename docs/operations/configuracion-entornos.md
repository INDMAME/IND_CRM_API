# Configuración de entornos

## Resumen

- Solo se soportan `DEV` y `PROD`.
- `App.config` queda como alternativa local.
- La configuración operativa se gestiona con variables de entorno de máquina.
- Todas las claves consumidas por la API pueden resolverse desde variables de entorno de máquina.
- Los secretos y valores críticos se cargan fuera del repositorio.
- Los scripts de configuración inicial exigen `TargetEnvironment` explícito para evitar valores predeterminados peligrosos.
- Los scripts de configuración inicial ya incluyen claves de servicio y HTTPS usadas por los `.bat`.
- Los scripts de configuración inicial gestionan también las claves usadas por `IND_CRM_APP`, incluida la URL pública de la web y Entra/OIDC.
- `INDCRM_BASE_URL` y `INDCRM_PUBLIC_*` describen la API; `INDCRM_WEB_BASE_URL` e `INDCRM_WEB_PUBLIC_*` describen la web.
- Si `IND_ENV` es `DEV` o `PROD`, la API exige `INDCRM_BASE_URL` con `https://`, exige `INDCRM_PUBLIC_HOST`/`INDCRM_PUBLIC_PORT`, valida que coincidan y no arranca con la alternativa local.
- Los `.bat` versionados no deben guardar contraseñas, usuarios personales ni secretos de certificados.

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

Qué hace:

- Crea o actualiza la configuración base del entorno.
- No pide valores por consola.
- Omite marcadores sensibles.
- Escribe también `ASPNETCORE_ENVIRONMENT` por entorno: `Development` para `DEV` y `Production` para `PROD`.
- Escribe las claves web no sensibles: `INDCRM_WEB_BASE_URL`, `INDCRM_WEB_PUBLIC_HOST`, `INDCRM_WEB_PUBLIC_PORT`, `IND_E2E_BASE_URL` y `ApiSettings__BaseUrl`.
- Muestra marcadores para las claves Entra/OIDC de la web y las omite hasta que se carguen con el script interactivo.
- Configura también `INDCRM_SERVICE_USER` e `INDCRM_HTTP_SERVICE_USER`.
- Incluye también valores operativos predeterminados de OpenAI, Azure Docs IA y tipos de cambio.
- Requiere indicar `-TargetEnvironment` de forma explícita.

Uso:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\set-indcrm-machine-env.ps1 -TargetEnvironment PROD
powershell -ExecutionPolicy Bypass -File .\scripts\set-indcrm-machine-env.ps1 -TargetEnvironment PROD -Apply
```

### `scripts/set-indcrm-machine-critical-env.ps1`

Qué hace:

- Pide y guarda los valores reales y sensibles.
- Sin `-Apply` solo muestra una vista previa.
- Con `-Apply` solicita los valores uno a uno.
- Incluye `INDCRM_SERVICE_PASSWORD` y la contraseña PFX del entorno objetivo.
- Incluye las claves Entra/OIDC usadas por `IND_CRM_APP`.
- Incluye `INDCRM_CONTEXT_TOKEN_SECRET_KEY` y la ruta PFX opcional del entorno objetivo.
- Requiere indicar `-TargetEnvironment` de forma explícita.

Uso:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\set-indcrm-machine-critical-env.ps1 -TargetEnvironment PROD
powershell -ExecutionPolicy Bypass -File .\scripts\set-indcrm-machine-critical-env.ps1 -TargetEnvironment PROD -Apply
```

### `scripts/set-indcrm-machine-all-env.ps1`

Qué hace:

- Pide y actualiza todas las variables de entorno operativas en un solo flujo.
- Usa valores predeterminados por entorno para URL, host, IP, configuración AX, blob y otras claves base.
- Incluye `ASPNETCORE_ENVIRONMENT` para que `IND_CRM_APP` no dependa del valor por defecto del host.
- Incluye los valores web predeterminados por entorno y evita que `ApiSettings__BaseUrl` quede desalineada respecto a `INDCRM_BASE_URL`.
- Incluye las claves Entra/OIDC requeridas por la web.
- Mantiene la lógica interactiva de `Enter` para conservar el valor actual o usar el valor predeterminado.
- Permite dejar vacías claves opcionales como `INDCRM_CORS_ALLOWED_ORIGINS` y `OPENAI_TRANSCRIPTION_DEFAULT_PROMPT`.
- Para vaciar una clave opcional existente, admite escribir `__CLEAR__`.
- Incluye también las claves de servicio y la contraseña PFX del entorno objetivo.
- Incluye la ruta PFX predeterminada del entorno objetivo.
- Requiere indicar `-TargetEnvironment` de forma explícita.

Uso:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\set-indcrm-machine-all-env.ps1 -TargetEnvironment PROD
powershell -ExecutionPolicy Bypass -File .\scripts\set-indcrm-machine-all-env.ps1 -TargetEnvironment PROD -Apply
```

### `bin\x86\Release\enable_https_2083_dev.bat`

Qué hace:

- Configura HTTPS de `HTTP.sys` para `https://dev.insertec.biz:2083/`.
- Exige una máquina ya configurada como `DEV` con `IND_ENV`, `INDCRM_BASE_URL`, `INDCRM_PUBLIC_HOST`, `INDCRM_PUBLIC_PORT` e `INDCRM_HTTP_SERVICE_USER` coherentes.
- Usa por defecto el PFX operativo `C:\INDAxaptaConfigAPI\dev.insertec.biz\dominio.pfx`.
- Permite sobrescribir la ruta mediante `INDCRM_DEV_PFX_PATH` o el primer argumento.
- Si no recibe la contraseña por argumento ni existe `INDCRM_DEV_PFX_PASSWORD`, la pide por consola de forma segura.
- Importa el certificado en `Cert:\LocalMachine\My`, valida que el certificado corresponde al host `dev.insertec.biz`, guarda una copia de seguridad del `sslcert` actual en `%TEMP%` y reaplica `urlacl` + `sslcert`.

Uso:

```bat
cmd /c .\bin\x86\Release\enable_https_2083_dev.bat
cmd /c .\bin\x86\Release\enable_https_2083_dev.bat "C:\otra\ruta\dominio.pfx"
```

Nota:

- Evitar pasar la contraseña como segundo argumento salvo automatización controlada.
- El script no descarga el PFX: espera un archivo local ya provisionado.
- `bin\x86\Release\enable_https_17776_dev.bat` y `bin\x86\Release\enable_https_7776_dev.bat` quedan como envoltorios de compatibilidad y redirigen al script de `2083`.

### `bin\x86\Release\enable_https_7776.bat`

Qué hace:

- Configura HTTPS de `HTTP.sys` para `https://crm.insertec.biz:7776/`.
- Exige una máquina ya configurada como `PROD` con `IND_ENV`, `INDCRM_BASE_URL`, `INDCRM_PUBLIC_HOST`, `INDCRM_PUBLIC_PORT` e `INDCRM_HTTP_SERVICE_USER` coherentes.
- Usa por defecto `certificados\dominio.pfx` dentro del árbol local del repositorio.
- Permite sobrescribir la ruta mediante `INDCRM_PROD_PFX_PATH` o el primer argumento.
- Si no recibe la contraseña por argumento ni existe `INDCRM_PROD_PFX_PASSWORD`, la pide por consola de forma segura.
- Importa el certificado en `Cert:\LocalMachine\My`, valida que el certificado corresponde al host `crm.insertec.biz`, guarda una copia de seguridad del `sslcert` actual en `%TEMP%` y reaplica `urlacl` + `sslcert`.

Uso:

```bat
cmd /c .\bin\x86\Release\enable_https_7776.bat
cmd /c .\bin\x86\Release\enable_https_7776.bat "C:\otra\ruta\dominio.pfx"
```

Nota:

- El script no descarga el PFX. Además, `certificados/` está ignorado por Git, así que ese secreto no viaja al capturar cambios.

### `bin\x86\Release\instalar_api_axapta.bat`

Qué hace:

- Instala el servicio Windows `IND_CRM_API` usando las credenciales de servicio de máquina.
- Exige `IND_ENV`, `INDCRM_PUBLIC_HOST` e `INDCRM_PUBLIC_PORT`; no asume `localhost:7776`.
- Muestra la URL local/pública solo después de resolver las claves de máquina.
- La fuente versionada está en `Bats`; MSBuild la copia a `bin\x86\Release` al compilar.

## Uso rápido

1. Cargar la base del entorno.
2. Cargar los valores críticos reales.
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

## Qué esperar en consola

- Script base: no pregunta nada; solo crea variables y omite marcadores.
- Script crítico sin `-Apply`: solo muestra una vista previa.
- Script crítico con `-Apply`: pide los valores uno a uno.
- Script unificado sin `-Apply`: muestra el valor actual y el predeterminado del entorno objetivo.
- Script unificado con `-Apply`: pide todas las variables en un solo recorrido.

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

### Críticas

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

### Valores operativos por defecto

- `USER_DEFAULT`: `APIAX`
- `AZURE_DOCS_IA_ENDPOINT`: `https://westeurope.api.cognitive.microsoft.com/`
- `OPENAI_EXPENSE_SHEET_ASK_MODEL`: `gpt-5.4-mini`
- `OPENAI_EXPENSE_SHEET_ASK_REASONING_EFFORT`: `low`
- `OPENAI_EXPENSE_TICKET_MODEL`: `gpt-5.4-nano`
- `INDCRM_CONTEXT_TOKEN_AUDIENCE`: `IND_CRM_WEB_CONTEXT`
- `INDCRM_CONTEXT_TOKEN_ISSUER`: `IND_CRM_CONTEXT`
- `INDCRM_CONTEXT_TOKEN_SECRET_KEY`: generado por el script al aplicar si la máquina no tiene valor previo; debe quedar distinto entre DEV y PROD.
- `ASPNETCORE_ENVIRONMENT`: `DEV` -> `Development`, `PROD` -> `Production`.
- `INDCRM_WEB_BASE_URL`: `DEV` -> `https://dev.insertec.biz:2053/`, `PROD` -> `https://crm.insertec.biz:7702/`
- `INDCRM_WEB_PUBLIC_HOST`: `DEV` -> `dev.insertec.biz`, `PROD` -> `crm.insertec.biz`
- `INDCRM_WEB_PUBLIC_PORT`: `DEV` -> `2053`, `PROD` -> `7702`
- `ApiSettings__BaseUrl`: mismo valor que `INDCRM_BASE_URL` para evitar overrides antiguos de la web.

### HTTPS opcionales por entorno

- `INDCRM_DEV_PFX_PATH`
- `INDCRM_DEV_PFX_PASSWORD`
- `INDCRM_PROD_PFX_PATH`
- `INDCRM_PROD_PFX_PASSWORD`

Valores predeterminados:

- `INDCRM_DEV_PFX_PATH`: `C:\INDAxaptaConfigAPI\dev.insertec.biz\dominio.pfx`
- `INDCRM_PROD_PFX_PATH`: `C:\INDAxaptaConfigAPI\crm.insertec.biz\dominio.pfx`

## Scripts locales no versionados

Para scripts privados de máquina o utilidades con datos sensibles, usar:

- `scripts/local/`
- `scripts/*.local.ps1`
- `scripts/*.secrets.ps1`

Esas rutas quedan ignoradas por Git.

## Notas

- `INDCRM_PUBLIC_HOST`, `INDCRM_PUBLIC_IP` y `INDCRM_PUBLIC_PORT` son datos operativos para DNS, firewall y despliegue.
- La IP de `DEV` queda confirmada en `192.168.0.148`.
- La web `DEV` se sirve en `https://dev.insertec.biz:2053/`; la API `DEV` se sirve en `https://dev.insertec.biz:2083/`.
- La web `PROD` se sirve en `https://crm.insertec.biz:7702/`; la API `PROD` se sirve en `https://crm.insertec.biz:7776/`.
- El puerto web se aplica realmente en IIS, pero `INDCRM_WEB_BASE_URL`, `INDCRM_WEB_PUBLIC_HOST` e `INDCRM_WEB_PUBLIC_PORT` quedan como contrato de máquina y son validados por `IND_CRM_APP\publish.ps1`.
- El cambio de `DEV` a `PROD` debe resolverse en despliegue, no en recompilación.
- `DEV` debe mantener `ASPNETCORE_ENVIRONMENT=Development`, `INDCRM_AX_CONFIG_FILE=...\CRM_API_AxConfig_DEV.axc` y `AZURE_BLOB_ENVIRONMENT_SEGMENT=DEV`; los scripts de instalación bloquean combinaciones cruzadas con `PROD`.
- Cuando se cambie una variable de máquina usada por la API, reiniciar `IND_CRM_API` para recargar la configuración del proceso.
- Blob usa `AZURE_BLOB_ENVIRONMENT_SEGMENT` y, si falta, hereda `IND_ENV` antes de usar una alternativa neutra.
- Un `git push` entre ramas no cambia el entorno por sí solo; el riesgo real es ejecutar scripts locales con variables equivocadas.
- Los scripts versionados en `Bats` se copian a `bin\x86\Release` al compilar. `instalar_api_axapta.bat`, `enable_https_2083_dev.bat`, `enable_https_17776_dev.bat`, `enable_https_7776_dev.bat` y `enable_https_7776.bat` reciben sus claves desde las variables de máquina preparadas por los `.ps1`.
