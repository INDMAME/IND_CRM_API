@echo off
setlocal
title Habilitar HTTPS IND_CRM_API 7776
cd /d "%~dp0"

REM ------------------------------------------------------
REM Configura HTTP.sys para HTTPS en el puerto 7776.
REM Ejecutar como Administrador.
REM Ajusta SERVICE_USER si el servicio corre con otra cuenta.
REM El certificado debe estar en LocalMachine\My.
REM ------------------------------------------------------

set "HOST=crm.insertec.biz"
set "PORT=7776"
REM Valores tomados del certificado crm_insertec_biz.crt y el GUID del ensamblado.
set "SERVICE_USER=INSERTEC\MARCO.MEZA"
set "CERT_THUMBPRINT=3A0E737433204494DA83F03056926A0B0290C3C8"
set "APP_ID={ABCBA743-3E22-4006-B8D1-4D7EA6B4F4ED}"

REM Importacion PFX (password fija solicitada por operacion)
set "PFX_PATH=%~dp0..\certificados\dominio.pfx"
set "PFX_PASSWORD=7pg39EQuB"

if not exist "%PFX_PATH%" (
    echo ERROR: PFX not found at %PFX_PATH%
    pause
    exit /b 1
)

echo Habilitando HTTPS para https://%HOST%:%PORT%/
echo Usuario del servicio: %SERVICE_USER%
echo Thumbprint: %CERT_THUMBPRINT%
echo AppId: %APP_ID%
echo.

REM Importar PFX a LocalMachine\My
powershell -NoProfile -Command "$pfxPath = '%PFX_PATH%'; $pw = ConvertTo-SecureString '%PFX_PASSWORD%' -AsPlainText -Force; Import-PfxCertificate -FilePath $pfxPath -CertStoreLocation Cert:\LocalMachine\My -Password $pw | Out-Null"
set "PFX_PASSWORD="

REM Remove existing bindings for this host (ignore errors)
netsh http delete sslcert ipport=0.0.0.0:%PORT% >nul 2>&1
netsh http delete urlacl url=https://%HOST%:%PORT%/ >nul 2>&1

REM URL ACL (HTTPS)
netsh http add urlacl url=https://%HOST%:%PORT%/ user="%SERVICE_USER%"

REM SSL binding (HTTP.sys)
netsh http add sslcert ipport=0.0.0.0:%PORT% certhash=%CERT_THUMBPRINT% appid="%APP_ID%" certstorename=MY

REM Firewall (opcional)
REM netsh advfirewall firewall add rule name="IND_CRM_API HTTPS %PORT%" dir=in action=allow protocol=TCP localport=%PORT%

echo.
echo Listo. Verifica con: netsh http show sslcert
pause
endlocal
