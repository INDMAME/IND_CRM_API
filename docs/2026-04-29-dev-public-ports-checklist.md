# DEV Public Ports Checklist

## Objetivo

Publicar `DEV` en Internet con host propio y puertos nuevos, manteniendo el mismo codigo fuente que `PROD` y separando el comportamiento por variables de maquina.

## URLs objetivo

| Componente | URL |
| --- | --- |
| WEB DEV | `https://dev.insertec.biz:17702/` |
| API DEV | `https://dev.insertec.biz:17776/` |

## Variables esperadas en la maquina DEV

- `IND_ENV=DEV`
- `ASPNETCORE_ENVIRONMENT=Production`
- `INDCRM_BASE_URL=https://dev.insertec.biz:17776/`
- `INDCRM_PUBLIC_HOST=dev.insertec.biz`
- `INDCRM_PUBLIC_PORT=17776`
- `AZURE_BLOB_ENVIRONMENT_SEGMENT=DEV`
- `INDCRM_CORS_ENABLED=false`

## Pasos API DEV

1. Ejecutar preview del entorno.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\set-indcrm-machine-all-env.ps1 -TargetEnvironment DEV
```

2. Aplicar variables reales en la maquina DEV.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\set-indcrm-machine-all-env.ps1 -TargetEnvironment DEV -Apply
```

3. Instalar o actualizar el servicio API.

```bat
cmd /c .\bin\x86\Release\instalar_api_axapta.bat
```

4. Configurar HTTPS de HTTP.sys para `17776`.

```bat
cmd /c .\bin\x86\Release\enable_https_17776_dev.bat "C:\INDAxaptaConfigAPI\dev.insertec.biz\dominio.pfx"
```

5. Reiniciar el servicio.

```powershell
Restart-Service IND_CRM_API
```

## Validaciones API DEV

```powershell
netsh http show urlacl url=https://dev.insertec.biz:17776/
netsh http show sslcert ipport=0.0.0.0:17776
curl.exe -k https://dev.insertec.biz:17776/api/health/ping
curl.exe -k https://dev.insertec.biz:17776/api/system/getEnvironmentName
```

## Validaciones WEB DEV

- IIS debe tener binding `https`, host `dev.insertec.biz`, puerto `17702` y certificado valido para `dev.insertec.biz`.
- La web debe resolver `INDCRM_BASE_URL=https://dev.insertec.biz:17776/`.
- Entra ID debe permitir `https://dev.insertec.biz:17702/signin-oidc`.

```powershell
curl.exe -k https://dev.insertec.biz:17702/
```

## Criterio de cierre

- `GET /api/health/ping` responde en `https://dev.insertec.biz:17776`.
- `GET /api/system/getEnvironmentName` devuelve `DEV`.
- Si falta `INDCRM_BASE_URL` o usa `http://`, la API no arranca en `IND_ENV=DEV`.
- La web abre en `https://dev.insertec.biz:17702`.
- Login Entra funciona en WEB DEV.
- Postman DEV usa `https://dev.insertec.biz:17776`.
- `PROD` conserva `https://crm.insertec.biz:7702` y `https://crm.insertec.biz:7776`.
