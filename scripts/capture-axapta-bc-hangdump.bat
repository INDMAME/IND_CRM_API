@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "PS_SCRIPT=%SCRIPT_DIR%capture-axapta-bc-hangdump.ps1"

if not exist "%PS_SCRIPT%" (
    echo Missing PowerShell script: "%PS_SCRIPT%"
    exit /b 1
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" %*
set "EXITCODE=%ERRORLEVEL%"

if not "%EXITCODE%"=="0" (
    echo.
    echo The hang dump or recycle operation failed. Exit code: %EXITCODE%
) else (
    echo.
    echo The hang dump sequence and recycle operation finished successfully.
)

exit /b %EXITCODE%
