@echo off
setlocal
title Instalador de IND_CRM_API
cd /d "%~dp0"

echo ======================================================
echo   Instalando servicio IND_CRM_API  (Axapta CRM API)
echo ======================================================
echo.

REM ------------------------------------------------------
REM Resolve public endpoint from machine variables first.
REM Keep this script safe to version in DEV and PROD.
REM ------------------------------------------------------
set "PORT=%INDCRM_PUBLIC_PORT%"
set "PUBLIC_HOST=%INDCRM_PUBLIC_HOST%"

if "%PORT%"=="" set "PORT=7776"
if "%PUBLIC_HOST%"=="" set "PUBLIC_HOST=localhost"

REM ------------------------------------------------------
REM Service credentials must stay outside the repository.
REM Load them from machine variables or an ignored local helper.
REM ------------------------------------------------------
set "SERVICE_USER=%INDCRM_SERVICE_USER%"
set "SERVICE_PASSWORD=%INDCRM_SERVICE_PASSWORD%"

if "%SERVICE_USER%"=="" (
    echo ERROR: INDCRM_SERVICE_USER is not defined.
    echo Define the service account outside the repo before installing.
    echo Example:
    echo   setx INDCRM_SERVICE_USER "INSERTEC\API_AXUSER"
    pause
    exit /b 1
)

if "%SERVICE_PASSWORD%"=="" (
    echo ERROR: INDCRM_SERVICE_PASSWORD is not defined.
    echo Define the service password outside the repo before installing.
    echo Example:
    echo   setx INDCRM_SERVICE_PASSWORD "<set_me>"
    pause
    exit /b 1
)

echo Public host: %PUBLIC_HOST%
echo Public port: %PORT%
echo Service user: %SERVICE_USER%
echo.

REM ------------------------------------------------------
REM Validate that the executable exists
REM ------------------------------------------------------
if not exist "IND_CRM_API.exe" (
    echo ERROR: IND_CRM_API.exe was not found in this folder.
    echo Copy this script into the same directory as the executable.
    pause
    exit /b 1
)

REM ------------------------------------------------------
REM Stop and remove the service if it already exists
REM ------------------------------------------------------
sc stop IND_CRM_API >nul 2>&1
sc delete IND_CRM_API >nul 2>&1

REM ------------------------------------------------------
REM Create the Windows service with local machine credentials
REM ------------------------------------------------------
echo Instalando servicio Windows...

sc create IND_CRM_API ^
    binPath= "%~dp0IND_CRM_API.exe" ^
    DisplayName= "IND CRM API" ^
    start= auto ^
    obj= "%SERVICE_USER%" ^
    password= "%SERVICE_PASSWORD%" >nul 2>&1

if errorlevel 1 (
    echo ERROR: No se pudo crear el servicio IND_CRM_API.
    echo Comprueba:
    echo   - Que la cuenta %SERVICE_USER% existe.
    echo   - Que has ejecutado este .bat como Administrador.
    echo   - Que la contrasena es correcta.
    pause
    exit /b 1
)

echo Servicio creado correctamente.
sc description IND_CRM_API "API REST de integracion CRM con Axapta (Business Connector)." >nul 2>&1
echo.

REM ------------------------------------------------------
REM Start the service
REM ------------------------------------------------------
echo Iniciando servicio...
net start IND_CRM_API >nul 2>&1

if errorlevel 1 (
    echo ERROR: No se pudo iniciar el servicio IND_CRM_API.
    echo Revisa el Visor de eventos o intenta iniciarlo manualmente.
) else (
    echo Servicio iniciado correctamente.
)

echo.
echo ======================================================
echo Servicio instalado e iniciado correctamente.
echo Puerto: %PORT%
echo ------------------------------------------------------
echo Acceso local:   http://localhost:%PORT%/swagger/ui/index
echo Acceso publico: https://%PUBLIC_HOST%:%PORT%/swagger/ui/index
echo ======================================================
echo.
pause
endlocal
