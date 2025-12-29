@echo off
setlocal
title Instalador de IND_CRM_API
cd /d "%~dp0"

echo ======================================================
echo   Instalando servicio IND_CRM_API  (Axapta CRM API)
echo ======================================================
echo.

REM ------------------------------------------------------
REM OBTENER PUERTO DESDE App.config (si existe)
REM ------------------------------------------------------
set "PORT="
set "BASE_URL="

if exist "App.config" (
    echo Leyendo BaseUrl desde App.config...
    for /f "tokens=2 delims== " %%a in ('findstr /i "BaseUrl" App.config') do set BASE_URL=%%a

    if not "%BASE_URL%"=="" (
        REM BASE_URL tipo: BaseUrl="http://+:7776/"
        for /f "tokens=2 delims=:" %%b in ("%BASE_URL%") do set PORT=%%~nb
        set PORT=%PORT:/=%
        set PORT=%PORT:"=%
        set PORT=%PORT:~0,-1%
    ) else (
        echo ADVERTENCIA: No se encontró la clave BaseUrl en App.config. Usando puerto por defecto 7776.
        set PORT=7776
    )
) else (
    echo ADVERTENCIA: No se encontró App.config. Usando puerto por defecto 7776.
    set PORT=7776
)

if "%PORT%"=="" set PORT=7776

echo Puerto detectado: %PORT%
echo.

REM ------------------------------------------------------
REM VALIDAR QUE EXISTA EL EJECUTABLE
REM ------------------------------------------------------
if not exist "IND_CRM_API.exe" (
    echo ❌ ERROR: No se encontró IND_CRM_API.exe en esta carpeta.
    echo Copia este script en el mismo directorio del ejecutable.
    pause
    exit /b
)


REM ------------------------------------------------------
REM DETENER Y ELIMINAR SERVICIO SI EXISTE
REM ------------------------------------------------------
sc stop IND_CRM_API >nul 2>&1
sc delete IND_CRM_API >nul 2>&1

REM ------------------------------------------------------
REM CREAR SERVICIO WINDOWS CON CREDENCIALES
REM ------------------------------------------------------
echo Instalando servicio Windows...

sc create IND_CRM_API ^
    binPath= "%~dp0IND_CRM_API.exe" ^
    DisplayName= "IND CRM API" ^
    start= auto ^
    obj= "INSERTEC\MARCO.MEZA" ^
    password= "LaMaMeSa@1113" >nul 2>&1

if errorlevel 1 (
    echo ❌ ERROR: No se pudo crear el servicio IND_CRM_API.
    echo Comprueba:
    echo   - Que la cuenta de dominio INSERTECT\API_AXUSER existe.
    echo   - Que has ejecutado este .bat como Administrador.
    echo   - Que la contraseña es correcta.
    pause
    exit /b
)

echo Servicio creado correctamente.
echo.

REM ------------------------------------------------------
REM INICIAR EL SERVICIO
REM ------------------------------------------------------
echo Iniciando servicio...
net start IND_CRM_API >nul 2>&1

if errorlevel 1 (
    echo ❌ ERROR: No se pudo iniciar el servicio IND_CRM_API.
    echo Revisa el Visor de eventos o intenta iniciarlo manualmente.
) else (
    echo ✔ Servicio iniciado correctamente.
)

echo.
echo ======================================================
echo Servicio instalado e iniciado correctamente.
echo Puerto: %PORT%
echo ------------------------------------------------------
echo Acceso local:   http://localhost:%PORT%/swagger/ui/index
echo Acceso publico: https://crm.insertec.biz:%PORT%/swagger/ui/index
echo ======================================================
echo.
pause
endlocal
