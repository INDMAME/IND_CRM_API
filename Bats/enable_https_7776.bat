@echo off
setlocal EnableExtensions
title Habilitar HTTPS IND_CRM_API 7776
cd /d "%~dp0"

REM ------------------------------------------------------
REM Configure HTTP.sys for PROD HTTPS on port 7776.
REM
REM Guardrails:
REM - Run this script as Administrator.
REM - The machine must already be configured as PROD.
REM - The password is prompted securely when it is not provided
REM   through a machine environment variable or an explicit argument.
REM ------------------------------------------------------

set "TARGET_ENV=PROD"
set "HOST=crm.insertec.biz"
set "PORT=7776"
set "SERVICE_USER=%INDCRM_HTTP_SERVICE_USER%"
set "ASPNETCORE_ENV=%ASPNETCORE_ENVIRONMENT%"
set "AX_CONFIG=%INDCRM_AX_CONFIG_FILE%"
set "BLOB_ENV=%AZURE_BLOB_ENVIRONMENT_SEGMENT%"
set "APP_ID={ABCBA743-3E22-4006-B8D1-4D7EA6B4F4ED}"
set "PFX_PATH_ENV_VAR=INDCRM_PROD_PFX_PATH"
set "PFX_PATH_SOURCE=default path"
set "PFX_PATH=%INDCRM_PROD_PFX_PATH%"
if not "%PFX_PATH%"=="" set "PFX_PATH_SOURCE=%PFX_PATH_ENV_VAR%"
if "%PFX_PATH%"=="" set "PFX_PATH=C:\INDAxaptaConfigAPI\crm.insertec.biz\dominio.pfx"
set "PFX_PASSWORD=%INDCRM_PROD_PFX_PASSWORD%"
set "BACKUP_FILE=%TEMP%\IND_CRM_API_https_%TARGET_ENV%_%PORT%_backup.txt"

REM Optional overrides:
REM   enable_https_7776.bat "C:\custom\path\dominio.pfx"
REM   enable_https_7776.bat "C:\custom\path\dominio.pfx" "password"
if not "%~1"=="" (
    set "PFX_PATH=%~1"
    set "PFX_PATH_SOURCE=first argument"
)
if not "%~2"=="" set "PFX_PASSWORD=%~2"

call :RequireAdmin || goto :fail
call :RequireExpectedEnvironment || goto :fail
call :RequireEnvironmentAlignment || goto :fail
call :RequireExpectedHostConfiguration || goto :fail
call :RequireServiceUser || goto :fail
call :RequirePfxPath || goto :fail

echo Configuring HTTPS for https://%HOST%:%PORT%/
echo URL ACL user: %SERVICE_USER%
echo PFX path: %PFX_PATH%
echo PFX path source: %PFX_PATH_SOURCE%
echo Certificate source mode: local file only, no automatic download.
echo AppId: %APP_ID%
echo.

call :ImportCertificateAndGetThumbprint || goto :fail

netsh http show sslcert ipport=0.0.0.0:%PORT% > "%BACKUP_FILE%" 2>&1
echo Previous sslcert state saved to: %BACKUP_FILE%
echo.

netsh http delete sslcert ipport=0.0.0.0:%PORT% >nul 2>&1
netsh http delete urlacl url=https://%HOST%:%PORT%/ >nul 2>&1

netsh http add urlacl url=https://%HOST%:%PORT%/ user="%SERVICE_USER%"
if errorlevel 1 (
    echo ERROR: Failed to create the PROD URL ACL for https://%HOST%:%PORT%/
    goto :fail
)

netsh http add sslcert ipport=0.0.0.0:%PORT% certhash=%CERT_THUMBPRINT% appid="%APP_ID%" certstorename=MY
if errorlevel 1 (
    echo ERROR: Failed to bind the PROD certificate to 0.0.0.0:%PORT%
    goto :fail
)

echo.
echo HTTPS PROD configured successfully.
echo Verify with:
echo   netsh http show urlacl url=https://%HOST%:%PORT%/
echo   netsh http show sslcert ipport=0.0.0.0:%PORT%
echo.
pause
endlocal
exit /b 0

:RequireAdmin
net session >nul 2>&1
if errorlevel 1 (
    echo ERROR: This script must run as Administrator.
    exit /b 1
)
exit /b 0

:RequireExpectedEnvironment
if "%IND_ENV%"=="" (
    echo ERROR: IND_ENV is not defined.
    echo Run scripts\set-indcrm-machine-all-env.ps1 -TargetEnvironment %TARGET_ENV% -Apply first.
    exit /b 1
)

if /I not "%IND_ENV%"=="%TARGET_ENV%" (
    echo ERROR: IND_ENV is "%IND_ENV%". This script only supports %TARGET_ENV%.
    exit /b 1
)
exit /b 0

:RequireEnvironmentAlignment
for %%I in ("%AX_CONFIG%") do set "AX_CONFIG_FILE=%%~nxI"

if /I not "%ASPNETCORE_ENV%"=="Production" (
    echo ERROR: ASPNETCORE_ENVIRONMENT must be Production for PROD. Current value: %ASPNETCORE_ENV%
    exit /b 1
)

if /I not "%BLOB_ENV%"=="PROD" (
    echo ERROR: AZURE_BLOB_ENVIRONMENT_SEGMENT must be PROD. Current value: %BLOB_ENV%
    exit /b 1
)

if /I not "%AX_CONFIG_FILE%"=="CRM_API_AxConfig_PROD.axc" (
    echo ERROR: INDCRM_AX_CONFIG_FILE must point to CRM_API_AxConfig_PROD.axc for PROD. Current value: %AX_CONFIG%
    exit /b 1
)
exit /b 0

:RequireExpectedHostConfiguration
if /I not "%INDCRM_PUBLIC_HOST%"=="%HOST%" (
    echo ERROR: INDCRM_PUBLIC_HOST must be %HOST%. Current value: %INDCRM_PUBLIC_HOST%
    exit /b 1
)

if not "%INDCRM_PUBLIC_PORT%"=="%PORT%" (
    echo ERROR: INDCRM_PUBLIC_PORT must be %PORT%. Current value: %INDCRM_PUBLIC_PORT%
    exit /b 1
)

if "%INDCRM_BASE_URL%"=="" (
    echo ERROR: INDCRM_BASE_URL is not defined.
    echo Run scripts\set-indcrm-machine-all-env.ps1 -TargetEnvironment %TARGET_ENV% -Apply first.
    exit /b 1
)

echo(%INDCRM_BASE_URL%| findstr /I /C:"https://%HOST%:%PORT%/" >nul
if errorlevel 1 (
    echo ERROR: INDCRM_BASE_URL must point to https://%HOST%:%PORT%/. Current value: %INDCRM_BASE_URL%
    exit /b 1
)
exit /b 0

:RequireServiceUser
if "%SERVICE_USER%"=="" (
    echo ERROR: INDCRM_HTTP_SERVICE_USER is not defined.
    echo Run scripts\set-indcrm-machine-all-env.ps1 -TargetEnvironment %TARGET_ENV% -Apply first.
    exit /b 1
)
exit /b 0

:RequirePfxPath
if not exist "%PFX_PATH%" (
    echo ERROR: PFX not found at %PFX_PATH%
    echo This script expects a local PFX file and does not download it automatically.
    echo For Git checkouts, remember that certificados\ is ignored by .gitignore.
    echo Define %PFX_PATH_ENV_VAR% or pass a custom path as the first argument.
    exit /b 1
)
exit /b 0

:ImportCertificateAndGetThumbprint
set "CERT_THUMBPRINT="
set "INDCRM_HTTPS_HOST=%HOST%"
set "INDCRM_HTTPS_PFX_PATH=%PFX_PATH%"
set "INDCRM_HTTPS_PFX_PASSWORD=%PFX_PASSWORD%"

for /f "usebackq delims=" %%I in (`powershell -NoProfile -Command ^
  "$ErrorActionPreference='Stop';" ^
  "$hostName = $env:INDCRM_HTTPS_HOST;" ^
  "$pfxPath = $env:INDCRM_HTTPS_PFX_PATH;" ^
  "$plainPassword = $env:INDCRM_HTTPS_PFX_PASSWORD;" ^
  "if ([string]::IsNullOrWhiteSpace($plainPassword)) { $securePassword = Read-Host 'Enter PROD PFX password' -AsSecureString } else { $securePassword = ConvertTo-SecureString $plainPassword -AsPlainText -Force };" ^
  "$importedCerts = Import-PfxCertificate -FilePath $pfxPath -CertStoreLocation Cert:\LocalMachine\My -Password $securePassword;" ^
  "$leafCert = $null;" ^
  "foreach ($candidate in $importedCerts) { if ($candidate.HasPrivateKey) { $leafCert = $candidate; break } };" ^
  "if ($null -eq $leafCert) { throw 'No imported certificate with private key was found.' };" ^
  "$sanParts = New-Object System.Collections.Generic.List[string];" ^
  "foreach ($extension in $leafCert.Extensions) { if ($extension.Oid.FriendlyName -eq 'Subject Alternative Name') { [void]$sanParts.Add($extension.Format($false)) } };" ^
  "$identity = ($leafCert.Subject + ';' + [string]::Join(';', $sanParts)).ToLowerInvariant();" ^
  "if ($identity -notlike ('*' + $hostName.ToLowerInvariant() + '*')) { throw ('The imported certificate does not match host ' + $hostName + '. Subject/SAN: ' + $identity) };" ^
  "Write-Output ($leafCert.Thumbprint.Replace(' ', ''))"`) do set "CERT_THUMBPRINT=%%I"

set "PFX_PASSWORD="
set "INDCRM_HTTPS_HOST="
set "INDCRM_HTTPS_PFX_PATH="
set "INDCRM_HTTPS_PFX_PASSWORD="

if not defined CERT_THUMBPRINT (
    echo ERROR: Failed to import the PROD certificate or resolve a valid thumbprint for %HOST%.
    exit /b 1
)

echo Thumbprint detected: %CERT_THUMBPRINT%
exit /b 0

:fail
echo.
echo HTTPS PROD configuration did not finish.
echo Review the error above and rerun the script after fixing the issue.
echo.
pause
endlocal
exit /b 1
