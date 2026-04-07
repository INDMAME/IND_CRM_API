# DEV Current Closure Checklist

Fecha: 2026-03-24
Actualizado: 2026-04-07

## Objetivo

Cerrar el `DEV` actual en esta maquina con:

- Variables de entorno ya apuntando a `DEV`.
- HTTPS de `https://dev.insertec.biz:7776/` con certificado correcto.
- Servicio Windows `IND_CRM_API` instalado y arrancando.
- Validaciones minimas de `ping`, `health` y `getEnvironmentName`.

## Estado verificado antes de ejecutar

- `IND_ENV=DEV`
- `INDCRM_AX_CONFIG_FILE=C:\INDAxaptaConfigAPI\CRM_API_AxConfig_DEV.axc`
- `INDCRM_BASE_URL=https://dev.insertec.biz:7776/`
- `INDCRM_PUBLIC_HOST=dev.insertec.biz`
- `INDCRM_PUBLIC_IP=192.168.0.146`
- `INDCRM_PUBLIC_PORT=7776`
- `AZURE_BLOB_ENVIRONMENT_SEGMENT=DEV`
- El servicio `IND_CRM_API` no esta instalado actualmente (`sc query` devuelve 1060).
- El binding SSL de `0.0.0.0:7776` existe, pero hoy usa un certificado `CN=crm.insertec.biz`.
- El PFX operativo esperado para DEV queda fuera del repo en `C:\INDAxaptaConfigAPI\dev.insertec.biz\dominio.pfx`, salvo override explicito.
- El artefacto ejecutable verificado hoy es `bin\x86\Debug\IND_CRM_API.exe`.
- El build `Release` falla en esta maquina porque falta `AxImp.exe` del SDK/Build Tools para la referencia COM de Axapta.

## Checklist ejecutable

### 1. Confirmar base de entorno DEV

Comando:

```powershell
$names = @(
  'IND_ENV',
  'INDCRM_AX_CONFIG_FILE',
  'INDCRM_BASE_URL',
  'INDCRM_PUBLIC_HOST',
  'INDCRM_PUBLIC_IP',
  'INDCRM_PUBLIC_PORT',
  'AZURE_BLOB_ENVIRONMENT_SEGMENT'
)

foreach ($name in $names) {
  $value = [Environment]::GetEnvironmentVariable($name, 'Machine')
  "{0}={1}" -f $name, $(if ([string]::IsNullOrWhiteSpace($value)) { '<empty>' } else { $value })
}

Get-Item 'C:\INDAxaptaConfigAPI\CRM_API_AxConfig_DEV.axc'
```

Evidencia esperada:

- Todas las variables anteriores deben resolver a valores `DEV`.
- Debe existir `C:\INDAxaptaConfigAPI\CRM_API_AxConfig_DEV.axc`.

### 2. Corregir HTTPS para DEV

Comando:

```powershell
netsh http show sslcert ipport=0.0.0.0:7776
netsh http show urlacl url=https://dev.insertec.biz:7776/

# El .bat usa por defecto C:\INDAxaptaConfigAPI\dev.insertec.biz\dominio.pfx
# y pide la password por consola si INDCRM_DEV_PFX_PASSWORD no existe.
cmd /c .\Bats\enable_https_7776_dev.bat

# Solo si hace falta override de ruta:
cmd /c .\Bats\enable_https_7776_dev.bat "C:\otra\ruta\dominio.pfx"

netsh http show sslcert ipport=0.0.0.0:7776
```

Comprobacion del certificado cargado:

```powershell
$ssl = netsh http show sslcert ipport=0.0.0.0:7776
$hashLine = $ssl | Select-String 'Certificate Hash'
$thumb = ($hashLine -split ':')[1].Trim()

Get-ChildItem Cert:\LocalMachine\My |
  Where-Object { $_.Thumbprint -eq $thumb } |
  Select-Object Subject, Thumbprint, NotAfter
```

Evidencia esperada:

- La reserva `https://dev.insertec.biz:7776/` debe existir.
- El certificado final debe ser `CN=dev.insertec.biz` o equivalente valido para ese host.
- Si sigue apareciendo `CN=crm.insertec.biz`, no pasar al paso siguiente.

### 3. Intentar build Release y documentar el resultado

Comando:

```powershell
& 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe' `
  'IND_CRM_API.csproj' `
  /p:Configuration=Release `
  /p:Platform=x86 `
  /t:Build
```

Evidencia esperada:

- Si hay SDK/Build Tools completos, debe generarse `bin\x86\Release\IND_CRM_API.exe`.
- Si falla con `MSB3091` y `AxImp.exe`, registrar el bloqueo y usar temporalmente el binario actual de `Debug/x86` hasta completar la maquina de build.

### 4. Instalar o reinstalar el servicio DEV actual

Nota:

- Este paso usa el artefacto disponible hoy.
- Si ya existe `bin\x86\Release\IND_CRM_API.exe`, cambiar la ruta y usar `Release`.
- Si no existe, usar `bin\x86\Debug\IND_CRM_API.exe`.

Comando:

```powershell
$exe = (Resolve-Path '.\bin\x86\Debug\IND_CRM_API.exe').Path
$user = [Environment]::GetEnvironmentVariable('INDCRM_SERVICE_USER', 'Machine')
$pass = [Environment]::GetEnvironmentVariable('INDCRM_SERVICE_PASSWORD', 'Machine')

sc.exe stop IND_CRM_API | Out-Null
sc.exe delete IND_CRM_API | Out-Null

sc.exe create IND_CRM_API `
  binPath= "`"$exe`"" `
  DisplayName= "CRM API DEV" `
  start= auto `
  obj= "$user" `
  password= "$pass"

sc.exe description IND_CRM_API "API REST de integracion CRM con Axapta (Business Connector)."
sc.exe start IND_CRM_API
sc.exe query IND_CRM_API
```

Evidencia esperada:

- El servicio debe quedar en `RUNNING`.
- Si falla al arrancar, revisar de inmediato:

```powershell
Get-ChildItem 'C:\INDAxaptaLogs' -File -ErrorAction SilentlyContinue |
  Sort-Object LastWriteTime -Descending |
  Select-Object -First 10 Name, LastWriteTime, Length
```

### 5. Validar endpoints minimos en DEV

Comando:

```powershell
curl.exe -k https://dev.insertec.biz:7776/api/health/ping

$username = [Environment]::GetEnvironmentVariable('USER_DEFAULT', 'Machine')
$password = [Environment]::GetEnvironmentVariable('USER_PASS_DEFAULT', 'Machine')
$loginBody = (@{ Username = $username; Password = $password } | ConvertTo-Json -Compress)

$loginRaw = curl.exe -k -s `
  -H "Content-Type: application/json" `
  -d $loginBody `
  https://dev.insertec.biz:7776/api/auth/login

$token = ($loginRaw | ConvertFrom-Json).data.token

curl.exe -k -s `
  -H "Authorization: Bearer $token" `
  https://dev.insertec.biz:7776/api/health/health

curl.exe -k -s `
  -H "Authorization: Bearer $token" `
  https://dev.insertec.biz:7776/api/system/getEnvironmentName
```

Evidencia esperada:

- `ping` debe responder correctamente.
- `health` debe responder autenticado.
- `getEnvironmentName` debe devolver `DEV`.

### 6. Cerrar deuda operativa detectada

Checklist:

- [ ] Tener un PFX real de `dev.insertec.biz` en `C:\INDAxaptaConfigAPI\dev.insertec.biz\dominio.pfx` o usar un override de ruta controlado.
- [ ] Corregir la discrepancia `CRM_API_AxConfig.axc` vs `CRM_API_AxConfig_PROD.axc` en scripts y documentacion.
- [ ] Completar la maquina de build con SDK/Build Tools para que `Release/x86` compile sin depender de `Debug`.
- [ ] Mantener Postman separado por entorno y usar la coleccion `DEV V01` por defecto para pruebas.

## Definicion de terminado

- [ ] El binding `:7776` usa certificado valido de `DEV`.
- [ ] El servicio `IND_CRM_API` esta instalado y en `RUNNING`.
- [ ] `GET /api/health/ping` responde en DEV.
- [ ] `GET /api/health/health` responde autenticado en DEV.
- [ ] `GET /api/system/getEnvironmentName` devuelve `DEV`.
- [ ] El artefacto usado para instalar esta identificado (`Release` si ya compila, o `Debug` documentado como temporal).
