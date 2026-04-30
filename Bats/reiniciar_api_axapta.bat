@echo off
cls
echo ======================================
echo REINICIAR SERVICIO IND_CRM_API
echo ======================================
echo.

SET SERVICE_NAME=IND_CRM_API

echo Deteniendo servicio...
sc stop %SERVICE_NAME% >nul

echo Iniciando servicio...
sc start %SERVICE_NAME%

echo Servicio reiniciado.
pause
