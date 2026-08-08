@echo off
REM ============================================================
REM stop-all.bat
REM ホストPC側のTrayAppを止め、VM(dicom-pacs-vm)をシャットダウン
REM するための一括終了バッチ。
REM
REM VM側の各サービス(PostgreSQL/IIS/NSSM各種)はすべて「自動」
REM 起動設定のWindowsサービスなので、OSシャットダウン時にOS側が
REM 自動的に正常停止してくれる。個別に nssm stop する必要はない。
REM
REM 注意: cmd.exeが日本語(Shift-JIS)のechoを取りこぼして誤動作
REM する事故が過去にあったため、echo等の出力メッセージは
REM 英語のみにしてある。日本語はREMコメントとしてのみ使用する。
REM ============================================================
setlocal enabledelayedexpansion

cd /d "%~dp0"

REM ------------------------------------------------------------
REM 0. 設定値
REM ------------------------------------------------------------

REM ★ start-all.batと同じ値にしてください（vmrunで状態確認する場合のみ使用）
set VMX_PATH=C:\path\to\your\vm.vmx

set VMRUN=C:\Program Files (x86)\VMware\VMware Workstation\vmrun.exe
if not exist "%VMRUN%" set VMRUN=C:\Program Files\VMware\VMware Workstation\vmrun.exe
set SSH_HOST=dicomvm

echo ============================================================
echo  dicom-tool-3 : stop-all
echo ============================================================

REM ------------------------------------------------------------
REM 1. ホストPC側のTrayAppを停止する
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
REM 2. VMをシャットダウンする（SSH経由）
REM    VM側サービスは自動起動設定のWindowsサービスなので、
REM    シャットダウン時にOSが自動的に正常停止してくれる。
REM ------------------------------------------------------------
echo [2/3] Shutting down the VM via SSH ...
ssh -o ConnectTimeout=10 -o BatchMode=yes %SSH_HOST% "shutdown /s /t 0"
if errorlevel 1 (
    echo WARNING: Could not reach the VM via SSH. It may already be off,
    echo or unreachable. Please check manually if needed.
    goto done
) else (
    echo Shutdown command sent. The VM (and all its auto-start services)
    echo will stop shortly.
)

REM ------------------------------------------------------------
REM 3. (任意) vmrunでVM状態を確認する
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
