# CLAUDE.md

このファイルは、このリポジトリで作業するClaude Code向けのプロジェクト固有の申し送り事項です。

## DICOM通信・Orthancのテストについて

「C-ECHO/C-STORE/C-FIND/C-MOVEを試したい」「OrthancのREST APIを使いたい」「DCMTKの
コマンドを教えて」といった依頼が来たら、まず
[`docs/dicom-testing-tools/`](./docs/dicom-testing-tools/README.md)を参照すること。
このホストPCにインストール済みのDCMTK・Orthanc・DVTk Storage SCU/SCP Emulatorについて、
実際に`dicom-pacs-vm`を相手に動作確認済みのコマンドがツールごとにまとめてある。

特に重要な前提:

- `dicom-tool-3`自身の`DicomTool.DicomScp`は、C-ECHO/C-STOREに加えC-FIND/C-MOVEのSCPにも
  対応済み(2026-08-13実装。`services/DicomTool.DicomScp/Services/DicomScpService.cs`が
  `IDicomCFindProvider`/`IDicomCMoveProvider`を実装)。対応階層はSTUDY/SERIESのみ。
  C-MOVEの転送先AEは`appsettings.json`系の`RemoteAeTitles`セクションに事前登録が必要
  (未登録だと`Refused: MoveDestinationUnknown`)。詳細は`docs/dicom-testing-tools/dcmtk.md`参照。
- OrthancのAEタイトル・ポート等の既定値は`docs/dicom-testing-tools/orthanc.md`参照
  (DICOM DIMSEはポート`4242`、REST APIは`8042`)。
- **VM上のdicom-tool-3から、このホストPC上の別のDICOMツール(Orthanc等)へC-MOVE/C-STORE
  しようとして通信が失敗する場合、まずホストPC側のWindowsファイアウォールを疑うこと。**
  既定で受信がブロックされていることが多い(実際に発生・解決済み)。対処法は
  `docs/dicom-testing-tools/README.md`の「VM⇔ホストPC間の通信」節参照。
- コマンドを新たに試して動作確認できたら、その都度これらのMarkdownに追記していくこと
  (毎回コマンドを一から調べ直す手間を減らすための資料のため)。
