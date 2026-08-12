@echo off
REM ============================================================
REM start-all.bat
REM Starts the VM (dicom-pacs-vm), waits for SSH, checks/starts
REM the VM-side services, and finally starts the TrayApp on this
REM PC.
REM
REM NOTE: This file must contain ASCII only (no Japanese), even
REM in comments. cmd.exe on this machine has repeatedly
REM misinterpreted multi-byte UTF-8 text as Shift-JIS, corrupting
REM the parsed script and executing garbage as commands. Keep
REM all text in this file plain ASCII.
REM ============================================================
setlocal enabledelayedexpansion

cd /d "%~dp0"

REM ------------------------------------------------------------
REM 0. Settings (this is the only section you should need to edit)
REM ------------------------------------------------------------

REM *** Edit this to point at your own VM's .vmx file ***
REM   To find it in VMware Workstation: right-click the VM in the
REM   Library, choose "Settings", the path is shown near the top
REM   of that window (or in the window title bar).
REM   Default VM storage location is usually under:
REM   %USERPROFILE%\Documents\Virtual Machines\
set VMX_PATH=D:\Programming\VMware\Windows Server 2025.vmx

set VMRUN=C:\Program Files (x86)\VMware\VMware Workstation\vmrun.exe
if not exist "%VMRUN%" set VMRUN=C:\Program Files\VMware\VMware Workstation\vmrun.exe
set SSH_HOST=dicomvm
set REMOTE_HOST_IP=192.168.93.128
set TRAYAPP_PROJECT=services\DicomTool.TrayApp
set SSH_WAIT_TIMEOUT_SEC=120

REM Full path to nssm.exe on the VM (matches the example in
REM VM construction guide chapter 16; change if you placed it
REM somewhere else).
set NSSM_REMOTE_PATH=C:\Tools\nssm.exe

echo ============================================================
echo  dicom-tool-3 : start-all
echo ============================================================

REM ------------------------------------------------------------
REM 1. Start the VM via vmrun (if available)
REM ------------------------------------------------------------
if exist "%VMRUN%" (
    echo [1/5] Starting VM via vmrun ...
    "%VMRUN%" -T ws start "%VMX_PATH%"
    if errorlevel 1 (
        echo WARNING: vmrun failed to start the VM. Check VMX_PATH in this file.
        echo Please start the VM manually from VMware Workstation, then press any key to continue.
        pause >nul
    )
) else (
    echo [1/5] vmrun.exe not found at: %VMRUN%
    echo Please start the VM manually from VMware Workstation.
    echo Waiting a moment before checking SSH connectivity ...
    pause >nul
)

REM ------------------------------------------------------------
REM 2. Wait for SSH to become reachable (retry loop with timeout)
REM ------------------------------------------------------------
echo [2/5] Waiting for SSH to become reachable on %SSH_HOST% (timeout: %SSH_WAIT_TIMEOUT_SEC%s) ...
set /a SSH_ELAPSED=0
set SSH_OK=0

:ssh_wait_loop
ssh -o ConnectTimeout=5 -o BatchMode=yes %SSH_HOST% "echo ready" >nul 2>&1
if not errorlevel 1 (
    set SSH_OK=1
    goto ssh_wait_done
)
if !SSH_ELAPSED! GEQ %SSH_WAIT_TIMEOUT_SEC% goto ssh_wait_done
echo   ... still waiting (%SSH_ELAPSED%s / %SSH_WAIT_TIMEOUT_SEC%s)
timeout /t 5 /nobreak >nul
set /a SSH_ELAPSED=SSH_ELAPSED+5
goto ssh_wait_loop

:ssh_wait_done
if "%SSH_OK%"=="1" (
    echo SSH is reachable.
) else (
    echo WARNING: SSH did not become reachable within %SSH_WAIT_TIMEOUT_SEC% seconds.
    echo The VM may still be booting. Skipping remote service checks.
    goto skip_remote_checks
)

REM ------------------------------------------------------------
REM 3. Check key service ports, start any NSSM services that
REM    are not already running
REM ------------------------------------------------------------
echo [3/5] Checking key service ports on the VM ...
ssh %SSH_HOST% "netstat -ano | findstr \":5030 :3100 :3200 :5230 :7233 :11112\""
echo (If any port above is missing, the following will try to start the matching service.)

echo Ensuring Windows services are started (no-op if already running) ...
ssh %SSH_HOST% "%NSSM_REMOTE_PATH% start DicomToolScp" >nul 2>&1
ssh %SSH_HOST% "%NSSM_REMOTE_PATH% start DicomToolWorker" >nul 2>&1
ssh %SSH_HOST% "%NSSM_REMOTE_PATH% start TemporalServer" >nul 2>&1
ssh %SSH_HOST% "%NSSM_REMOTE_PATH% start DicomToolWorklist" >nul 2>&1
ssh %SSH_HOST% "%NSSM_REMOTE_PATH% start DicomToolViewer" >nul 2>&1
echo Service start attempts finished (services already running will just report already-started).

:skip_remote_checks

REM ------------------------------------------------------------
REM 4. Start TrayApp on this PC (only if not already running)
REM ------------------------------------------------------------
echo [4/5] Checking DicomTool.TrayApp on this PC ...
tasklist /fi "imagename eq DicomTool.TrayApp.exe" 2>nul | findstr /i "DicomTool.TrayApp.exe" >nul
if not errorlevel 1 (
    echo DicomTool.TrayApp is already running. Skipping.
) else (
    echo Starting DicomTool.TrayApp with RemoteHost=%REMOTE_HOST_IP% ...
    REM NOTE: nesting multiple levels of double quotes inside a
    REM "start" command argument confuses cmd.exe's parser, so we
    REM generate a small temp launcher .bat first and open that
    REM instead (avoids the quote-nesting problem entirely).
    set "TRAYAPP_LAUNCHER=%TEMP%\dicomtool_start_trayapp.bat"
    > "!TRAYAPP_LAUNCHER!" (
        echo @echo off
        echo set RemoteHost=%REMOTE_HOST_IP%
        echo cd /d "%~dp0%TRAYAPP_PROJECT%"
        echo dotnet run
    )
    start "DicomTool.TrayApp" "!TRAYAPP_LAUNCHER!"
)

REM ------------------------------------------------------------
REM 5. Print URLs and finish
REM ------------------------------------------------------------
echo [5/5] Done. Useful URLs:
echo   Worklist       : http://%REMOTE_HOST_IP%:3100
echo   Viewer         : http://%REMOTE_HOST_IP%:3200
echo   Timeline       : http://%REMOTE_HOST_IP%:5230
echo   Backend API    : http://%REMOTE_HOST_IP%:5030/graphql
echo   DICOM SCP Mgmt : http://%REMOTE_HOST_IP%:8090/swagger
echo   Temporal Web UI: http://%REMOTE_HOST_IP%:8233
echo ============================================================
echo Press any key to close this window (the URLs above stay visible until then).
pause >nul

endlocal
