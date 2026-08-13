# DVTk Storage SCU/SCP Emulator リファレンス

> **状態: インストール済み、ただしGUI操作が必要なため自動テストは未実施。**
> 実行ファイルは以下の場所にある(ポータブル形式で配置されている。`Program Files`配下では
> ない点に注意)。

```text
D:\Programming\lerning\Storage SCU Emulator.exe
D:\Programming\lerning\Storage SCP Emulator.exe
```

## Claude Codeでは自動テストできない理由

DVTkのStorage SCU/SCP Emulatorは、DCMTKのようなCLIツールではなく**GUI(Windows Forms)
アプリ**。起動しても自動では何もせず、「セッション(.ses)ファイルを読み込む」「Listen開始/
Send実行ボタンを押す」といった操作をGUI上で行って初めて通信が始まる。Claude Codeが動いている
この環境には画面を見てクリックする手段が無いため、**この2ツールについてはユーザー自身が
GUIを操作してテストする必要がある。** 以下は、実際にそのテストを行うための手順・既定値の
リファレンス。

## 既定値(セッションファイルの中身から確認済み)

`C:\Users\<ユーザー名>\Documents\DVTk\Storage SCP Emulator\StorageSCPEmulator.ses`・
`...\Storage SCU Emulator\StorageSCUEmulator.ses`という設定ファイルが最初から用意されており、
中身はテキストなので実行しなくても既定値が読み取れた。

| ツール | 自分(DVT)側のAEタイトル | 相手(SUT)側に期待するAEタイトル | ポート |
| --- | --- | --- | --- |
| Storage SCP Emulator | `DVTK_STR_SCP` | `DVTK_STR_SCU` | `104`(要管理者権限) |
| Storage SCU Emulator | `DVTK_STR_SCU` | `DVTK_STR_SCP` | `104` |

**注意:** ポート`104`はDICOM標準の特権ポート(1024未満)で、Windowsでlistenするには管理者権限が
必要。dicom-tool-3(`11112`)やOrthanc(`4242`)のように非特権ポートに変更してから使うことを
推奨(GUIの設定画面、またはセッションファイルの`SUT-PORT`/`DVT-PORT`行を直接編集する)。

## 実際に試したこと・分かったこと

- `Storage SCP Emulator.exe`をダブルクリックで起動できることは確認済み(ウィンドウが開く)。
- ただし起動しただけでは何もlistenを始めない(`netstat`で確認済み。セッションを開いて
  明示的に「開始」操作をするまで待受は始まらない)。

## 手動でテストする手順(ユーザー向け)

### 1. dicom-tool-3宛てにC-STOREを送ってみる(Storage SCU Emulator)

1. `Storage SCU Emulator.exe`を起動する。
2. `File > Open Session`等から`StorageSCUEmulator.ses`を読み込む(既定で自動で読み込まれる
   実装のことも多い)。
3. 設定画面で以下に変更する:
   - Remote(SUT) AE Title: `DICOMTOOL3`
   - Remote(SUT) Hostname: `192.168.93.128`
   - Remote(SUT) Port: `11112`
4. 送信するDICOMファイルとして、`services/DicomTool.DicomScp/SampleData/sample1.dcm`等を
   指定する。
5. Send(送信)を実行する。
6. 結果を確認する方法:
   - GUI上のログでStatus`Success`になっているか。
   - `curl -X POST http://192.168.93.128:5030/graphql -H "Content-Type: application/json" -d "{\"query\":\"{ studies { patientId studyInstanceUid } }\"}"`
     を実行し、新しいStudyが登録されているか(このプロジェクト側からの検証方法。
     [dcmtk.md](./dcmtk.md)と同じ確認パターン)。

### 2. dicom-tool-3からのC-STOREをDVTk側で受信してみる(Storage SCP Emulator)

1. `Storage SCP Emulator.exe`を起動し、`StorageSCPEmulator.ses`を読み込む。
2. Listen Port等を非特権ポート(例: `11113`)に変更し、Listen(待受)を開始する。
3. このPC上でDCMTKの`storescu`を使い、DVTk側へC-STOREを送る:

   ```text
   storescu -v -aet DICOMTOOL3SCU -aec DVTK_STR_SCP localhost 11113 services\DicomTool.DicomScp\SampleData\sample1.dcm
   ```

   (`-aec`はDVTk側のAEタイトルに、DVTk側のGUI設定と一致させること)
4. DVTk側のGUIログに受信イベントが表示されることを確認する。

### 3. dicom-tool-3自身からDVTk SCP Emulatorへ実際にC-MOVEしてみる(応用)

手順2でDVTk SCP Emulatorが`11113`でlisten済みの状態にした上で、`appsettings.Production.json`
(VM上、`C:\apps\DicomTool.DicomScp\`)の`RemoteAeTitles`セクションに以下を追記し
サービスを再起動すれば、dicom-tool-3からDVTk SCP Emulator宛てのC-MOVEも試せる
(ホストPC側のファイアウォールで該当ポートの受信許可が必要な点は
[dcmtk.md「よくあるハマりポイント」](./dcmtk.md#よくあるハマりポイントvmホストpc間の通信)
を参照)。

```json
"RemoteAeTitles": {
  "DVTK_STR_SCP": "192.168.93.1:11113"
}
```

```text
movescu -v -S -aet DICOMTOOL3SCU -aec DICOMTOOL3 --move DVTK_STR_SCP -k "0008,0052=STUDY" -k "0020,000d=<StudyInstanceUID>" 192.168.93.128 11112
```

## 参考: DVTk DICOM Network Analyzerについて

もう1つ、`DVTk-DICOM-Network-Analyzer-V5.3.0.msi`という3つ目のインストーラーも
`Downloads`フォルダに見つかっている(Wiresharkに近い、DICOM通信のパケットキャプチャ/
解析ツール)。今回のC-ECHO/C-STORE/C-FIND/C-MOVEの疎通確認そのものには必須ではないが、
「なぜアソシエーションが拒否されるのか」等を深掘りしたい時に有用。こちらもGUIツールのため
同様にユーザー自身での操作が必要。
