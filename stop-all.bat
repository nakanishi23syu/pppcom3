@echo off
REM ============================================================
REM stop-all.bat
REM Stops the TrayApp on this PC, then shuts down the VM
REM (dicom-pacs-vm) over SSH.
REM
REM All VM-side services (PostgreSQL/IIS/NSSM-managed) are set
REM to auto-start, so Windows shutdown stops them cleanly on its
REM own. No need to stop each one individually.
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
REM 0. Settings
REM ------------------------------------------------------------

REM Keep this the same as VMX_PATH in start-all.bat (only used
REM for the optional vmrun power-state check below).
set VMX_PATH=D:\Programming\VMware\Windows Server 2025.vmx

set VMRUN=C:\Program Files (x86)\VMware\VMware Workstation\vmrun.exe
if not exist "%VMRUN%" set VMRUN=C:\Program Files\VMware\VMware Workstation\vmrun.exe
set SSH_HOST=dicomvm

echo ============================================================
echo  dicom-tool-3 : stop-all
echo ============================================================

REM ------------------------------------------------------------
REM 1. Stop TrayApp on this PC
REM ------------------------------------------------------------
echo [1/3] Stopping DicomTool.TrayApp on this PC (if running) ...
tasklist /fi "imagename eq DicomTool.TrayApp.exe" 2>nul | findstr /i "DicomTool.TrayApp.exe" >nul
if not errorlevel 1 (
    taskkill /im "DicomTool.TrayApp.exe" /f >nul 2>&1
    echo DicomTool.TrayApp stopped.
) else (
    echo DicomTool.TrayApp was not running. Skipping.
)

REM ------------------------------------------------------------
REM 2. Shut down the VM over SSH.
REM    VM-side services are all auto-start Windows services, so
REM    OS shutdown stops them cleanly by itself.
REM ------------------------------------------------------------
echo [2/3] Shutting down the VM via SSH ...
REM /f is required here: the VM normally has an active interactive
REM console session (someone logged into the VM's own desktop,
REM e.g. via the VMware console window). Without /f, Windows waits
REM for that session's programs to close gracefully and can show a
REM confirmation prompt ON THE VM'S OWN SCREEN that nobody is
REM watching, so the shutdown appears to just hang forever. /f
REM forcibly closes running applications without prompting.
ssh -o ConnectTimeout=10 -o BatchMode=yes %SSH_HOST% "shutdown /s /f /t 0"
if errorlevel 1 (
    echo WARNING: Could not reach the VM via SSH. It may already be off,
    echo or unreachable. Please check manually if needed.
    goto done
) else (
    echo Shutdown command sent. The VM and all its auto-start services
    echo will stop shortly.
)

REM ------------------------------------------------------------
REM 3. (optional) Confirm VM power-off via vmrun
REM ------------------------------------------------------------
if exist "%VMRUN%" (
    echo [3/3] Waiting for the VM to power off (checking via vmrun) ...
    set WAIT_ELAPSED=0
    set VM_STOPPED=0

    :vm_stop_wait_loop
    "%VMRUN%" -T ws list | findstr /i "%VMX_PATH%" >nul
    if errorlevel 1 (
        set VM_STOPPED=1
        goto vm_stop_wait_done
    )
    if !WAIT_ELAPSED! GEQ 60 goto vm_stop_wait_done
    timeout /t 5 /nobreak >nul
    set /a WAIT_ELAPSED=WAIT_ELAPSED+5
    goto vm_stop_wait_loop

    :vm_stop_wait_done
    if "%VM_STOPPED%"=="1" (
        echo VM is now powered off.
    ) else (
        echo VM still appears to be running (or vmrun list did not confirm in time).
        echo This is not necessarily a problem; Windows shutdown can take a while.
    )
) else (
    echo [3/3] vmrun.exe not found. Skipping VM power-state check.
    echo (This is optional; the shutdown command was already sent in step 2.)
)

:done
echo ============================================================
echo  stop-all finished.
echo ============================================================

endlocal
