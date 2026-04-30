@echo off
setlocal EnableExtensions
cd /d "%~dp0"
echo DEV now uses https://dev.insertec.biz:17776/.
echo Redirecting to enable_https_17776_dev.bat.
echo.
call "%~dp0enable_https_17776_dev.bat" %*
set "EXIT_CODE=%ERRORLEVEL%"
endlocal & exit /b %EXIT_CODE%
