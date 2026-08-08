@echo off
REM ============================================================
REM start-all.bat
REM VM(dicom-pacs-vm)を起動し、SSH疎通を待ち、VM側サービスの
REM 生死を確認・必要なら起動し、最後にホストPC側のTrayAppを
REM 起動するための一括起動バッチ。
REM
REM 注意: cmd.exeが日本語(Shift-JIS)のechoを取りこぼして誤動作
REM する事故が過去にあったため、echo等の出力メッセージは
REM 英語のみにしてある。日本語はREMコメントとしてのみ使用する。
REM ============================================================
setlocal enabledelayedexpansion

cd /d "%~dp0"

REM ------------------------------------------------------------
REM 0. 設定値（環境に合わせて書き換えるのはここだけでよい）
REM ------------------------------------------------------------

REM ★★★ ここを、ご自身のVMのvmxファイルパスに書き換えてください ★★★
REM   VMware Workstationでvmxファイルの場所を確認する方法:
REM   VMware Workstationのライブラリでこの仮想マシンを右クリック
REM   →「設定」→ 上部タブに表示されるパス、もしくは
REM   仮想マシンを選択した状態で「編集」→「仮想マシン設定」の
REM   ウィンドウタイトルバーにフルパスが表示されることが多い。
REM   （既定の保存先は %USERPROFILE%\Documents\Virtual Machines\ 配下）
set VMX_PATH=C:\path\to\your\vm.vmx

set VMRUN=C:\Program Files (x86)\VMware\VMware Workstation\vmrun.exe
if not exist "%VMRUN%" set VMRUN=C:\Program Files\VMware\VMware Workstation\vmrun.exe
set SSH_HOST=dicomvm
set REMOTE_HOST_IP=192.168.93.128
set TRAYAPP_PROJECT=services\DicomTool.TrayApp
set SSH_WAIT_TIMEOUT_SEC=120

REM VM上のnssm.exeのフルパス（VM構築手順.md 16章の例に合わせた既定値。
REM 実際に配置した場所が違う場合はここを書き換える）
set NSSM_REMOTE_PATH=C:\Tools\nssm.exe

echo ============================================================
echo  dicom-tool-3 : start-all
echo ============================================================

REM ------------------------------------------------------------
REM 1. VMware WorkstationでVMを起動する（vmrunがある場合のみ）
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
REM 2. SSH疎通待ち（リトライループ、タイムアウトあり）
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
REM 3. 主要ポートの生死確認＋必要ならNSSMサービス起動
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
REM 4. ホストPC側のTrayAppを起動する（未起動の場合のみ）
REM ------------------------------------------------------------
echo [4/5] Checking DicomTool.TrayApp on this PC ...
tasklist /fi "imagename eq DicomTool.TrayApp.exe" 2>nul | findstr /i "DicomTool.TrayApp.exe" >nul
if not errorlevel 1 (
    echo DicomTool.TrayApp is already running. Skipping.
) else (
    echo Starting DicomTool.TrayApp with RemoteHost=%REMOTE_HOST_IP% ...
    REM 注意: startコマンドの引数内で複数階層の二重引用符をネストすると
    REM cmd.exeが誤解釈することがあるため、一時的な起動用batを生成してから
    REM それをstartで開く方式にしている（引用符ネスト事故を避けるため）。
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
REM 5. URL一覧を表示して終了
REM ------------------------------------------------------------
echo [5/5] Done. Useful URLs:
echo   Worklist       : http://%REMOTE_HOST_IP%:3100
echo   Viewer         : http://%REMOTE_HOST_IP%:3200
echo   Timeline       : http://%REMOTE_HOST_IP%:5230
echo   Backend API    : http://%REMOTE_HOST_IP%:5030/graphql
echo   DICOM SCP Mgmt : http://%REMOTE_HOST_IP%:8090/swagger
echo   Temporal Web UI: http://%REMOTE_HOST_IP%:8233
echo ============================================================

endlocal
