@echo off
setlocal
title Habilitar HTTPS IND_CRM_API 7776
cd /d "%~dp0"

REM ------------------------------------------------------
REM Configure HTTP.sys for PROD HTTPS on port 7776.
REM Run this script as Administrator.
REM Secrets must stay outside the repository.
REM ------------------------------------------------------

set "HOST=crm.insertec.biz"
set "PORT=7776"
set "SERVICE_USER=%INDCRM_HTTP_SERVICE_USER%"
set "APP_ID={ABCBA743-3E22-4006-B8D1-4D7EA6B4F4ED}"

REM Default PROD certificate values. Override with:
REM   enable_https_7776.bat "C:\path\to\dominio.pfx" "password"
set "PFX_PATH=%~dp0..\certificados\dominio.pfx"
set "PFX_PASSWORD=%INDCRM_PROD_PFX_PASSWORD%"

if not "%~1"=="" set "PFX_PATH=%~1"
if not "%~2"=="" set "PFX_PASSWORD=%~2"

if "%SERVICE_USER%"=="" set "SERVICE_USER=%USERDOMAIN%\%USERNAME%"

if "%PFX_PASSWORD%"=="" (
    echo ERROR: Missing PROD PFX password.
    echo Provide it as the second argument or define INDCRM_PROD_PFX_PASSWORD locally.
    pause
    exit /b 1
)

if not exist "%PFX_PATH%" (
    echo ERROR: PFX not found at %PFX_PATH%
    pause
    exit /b 1
)

echo Habilitando HTTPS para https://%HOST%:%PORT%/
echo Usuario para URL ACL: %SERVICE_USER%
echo PFX: %PFX_PATH%
echo AppId: %APP_ID%
echo.

REM Import the PFX and capture the thumbprint of the leaf certificate.
set "CERT_THUMBPRINT="
for /f "usebackq delims=" %%I in (`powershell -NoProfile -Command "$ErrorActionPreference='Stop'; $pfxPath = '%PFX_PATH%'; $pw = ConvertTo-SecureString '%PFX_PASSWORD%' -AsPlainText -Force; $certs = Import-PfxCertificate -FilePath $pfxPath -CertStoreLocation Cert:\LocalMachine\My -Password $pw; $cert = $certs | Where-Object { $_.HasPrivateKey } | Select-Object -First 1; if ($null -eq $cert) { throw 'No imported certificate with private key was found.' }; $cert.Thumbprint"`) do set "CERT_THUMBPRINT=%%I"
set "PFX_PASSWORD="

if not defined CERT_THUMBPRINT (
    echo ERROR: Failed to import the PROD certificate or read its thumbprint.
    pause
    exit /b 1
)

echo Thumbprint detectado: %CERT_THUMBPRINT%
echo.

REM Remove the previous binding for this port and host.
netsh http delete sslcert ipport=0.0.0.0:%PORT% >nul 2>&1
netsh http delete urlacl url=https://%HOST%:%PORT%/ >nul 2>&1

REM URL ACL for the PROD host.
netsh http add urlacl url=https://%HOST%:%PORT%/ user="%SERVICE_USER%"

REM SSL binding for HTTP.sys using the imported certificate.
netsh http add sslcert ipport=0.0.0.0:%PORT% certhash=%CERT_THUMBPRINT% appid="%APP_ID%" certstorename=MY

REM Optional firewall rule.
REM netsh advfirewall firewall add rule name="IND_CRM_API HTTPS %PORT%" dir=in action=allow protocol=TCP localport=%PORT%

echo.
echo Listo. Verifica con: netsh http show sslcert
pause
endlocal
