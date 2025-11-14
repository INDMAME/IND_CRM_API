@echo off
cls
echo ============================================
echo   DESINSTALADOR SERVICIO IND_CRM_API
echo ============================================
echo.

SET SERVICE_NAME=IND_CRM_API

REM ------------------------------------------------------
REM Verificar si el servicio existe realmente
REM ------------------------------------------------------
sc query "%SERVICE_NAME%" >nul 2>&1

if %errorlevel% neq 0 (
    echo ❌ El servicio "%SERVICE_NAME%" NO existe.
    echo Nada que desinstalar.
    pause
    exit /b
)

echo ✔ Servicio encontrado. Intentando detenerlo...

REM ------------------------------------------------------
REM Detener el servicio si está en ejecución
REM ------------------------------------------------------
sc stop "%SERVICE_NAME%" >nul 2>&1

REM Pausar medio segundo
ping 127.0.0.1 -n 2 >nul

REM ------------------------------------------------------
REM ELIMINAR EL SERVICIO
REM ------------------------------------------------------
echo Eliminando servicio "%SERVICE_NAME%"...
sc delete "%SERVICE_NAME%" >nul 2>&1

REM Esperar a que Windows lo elimine
ping 127.0.0.1 -n 2 >nul

REM ------------------------------------------------------
REM VERIFICAR SI SE ELIMINÓ
REM ------------------------------------------------------
sc query "%SERVICE_NAME%" >nul 2>&1

if %errorlevel% equ 0 (
    echo ❌ ERROR: Windows NO eliminó el servicio.
    echo Puede estar:
    echo   - En estado de eliminación pendiente
    echo   - Con archivos bloqueados
    echo   - O el nombre interno es distinto
    echo.
    echo Prueba:
    echo   → Servicios de Windows (services.msc)
    echo   → Regedit: HKLM\SYSTEM\CurrentControlSet\Services
    pause
    exit /b
) else (
    echo ✔ Servicio eliminado correctamente.
)

echo.
pause
