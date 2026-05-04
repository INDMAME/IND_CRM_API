@echo off
setlocal EnableExtensions
cd /d "%~dp0"
echo DEV now uses https://dev.insertec.biz:2083/ for the API.
echo Redirecting to enable_https_2083_dev.bat.
echo.
call "%~dp0enable_https_2083_dev.bat" %*
set "EXIT_CODE=%ERRORLEVEL%"
endlocal & exit /b %EXIT_CODE%
