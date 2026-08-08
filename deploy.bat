@echo off
setlocal

cd /d "%~dp0"

set "WINSCP=C:\Program Files (x86)\WinSCP\WinSCP.com"
if not exist "%WINSCP%" set "WINSCP=C:\Program Files\WinSCP\WinSCP.com"

if not exist "%WINSCP%" (
    echo WinSCP.com not found.
    echo Please check your WinSCP install path and edit the WINSCP variable in this file.
    pause
    exit /b 1
)

"%WINSCP%" /script=deploy.txt
set "RESULT=%ERRORLEVEL%"

if "%RESULT%" neq "0" (
    echo Deploy FAILED. Error code: %RESULT%
) else (
    echo Deploy completed successfully.
)

pause
endlocal
