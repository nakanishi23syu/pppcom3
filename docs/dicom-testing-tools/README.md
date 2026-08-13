# DICOM通信テストツール ― コマンドリファレンス

会社の検証作業と同じように、DICOM通信(C-ECHO/C-STORE/C-FIND/C-MOVE)やOrthancのRESTful APIを
毎回コマンドから調べ直すのが面倒、という課題に対応するためのリファレンス集です。
このPC(ホストPC、VMではない)にインストール済みの4ツールについて、**実際に
`dicom-pacs-vm`(dicom-tool-3)を相手に動作確認したコマンド**をまとめています。

## このリポジトリでの検証記録

- 検証日: 2026-08-13
- 対象VM: `dicom-pacs-vm`(`192.168.93.128`)、`start-all.bat`で起動
- 対象サービス: `DicomTool.DicomScp`(AEタイトル`DICOMTOOL3`、ポート`11112`）
- 2026-08-13に`DicomTool.DicomScp`へC-FIND/C-MOVEのSCP実装を追加し、以下の「dicom-tool-3
  自身は何に対応しているか」の内容が更新されている(追加前はC-ECHO/C-STOREのみ対応だった)。

## 各ツールのドキュメント

| ツール | 状態 | ドキュメント |
| --- | --- | --- |
| DCMTK(`echoscu`/`storescu`/`findscu`/`movescu`等) | 動作確認済み | [dcmtk.md](./dcmtk.md) |
| Orthanc(REST API) | 動作確認済み | [orthanc.md](./orthanc.md) |
| DVTk Storage SCU/SCP Emulator | インストール済み。GUI操作が必要なためユーザー自身でのテストが前提 | [dvtk.md](./dvtk.md) |

## 前提知識: dicom-tool-3自身は何に対応しているか

`dicom-tool-3`の`DicomTool.DicomScp`(`services/DicomTool.DicomScp/Services/DicomScpService.cs`)は、
以下のサービスクラスすべてに対応しています(C-FIND/C-MOVEは2026-08-13に追加実装)。

| サービスクラス | dicom-tool-3の対応状況 |
| --- | --- |
| C-ECHO(疎通確認) | 対応 |
| C-STORE(画像受信、SCP側) | 対応 |
| C-FIND(検索) | 対応。**STUDY階層・SERIES階層のみ**(PATIENT/IMAGE階層は0件ヒット扱い) |
| C-MOVE(移動依頼、SCPとして受ける側) | 対応。転送先は`appsettings.json`の`RemoteAeTitles`に事前登録が必要 |

C-MOVEの転送先(宛先AE)は、Orthancの「モダリティ一覧」と同じ考え方で、`services/
DicomTool.DicomScp`側の`appsettings.json`/`appsettings.Development.json`/
`appsettings.Production.json`(VM上、Git管理外)の`RemoteAeTitles`セクションに
`"AEタイトル": "host:port"`という形で事前登録しておく必要があります。未登録のAEタイトルへ
C-MOVEしようとすると`Refused: MoveDestinationUnknown`で失敗します。詳細・実例は
[dcmtk.md](./dcmtk.md)を参照してください。

## 主要な既定値一覧

| 対象 | AEタイトル | ホスト | DICOMポート | HTTP/RESTポート |
| --- | --- | --- | --- | --- |
| dicom-tool-3(`DicomTool.DicomScp`) | `DICOMTOOL3` | `192.168.93.128`(VM上) | `11112` | `8090`(管理API/Swagger、DICOMwebではない) |
| dicom-tool-3 自己疎通テスト用SCU | `DICOMTOOL3SCU` | - | - | - |
| Orthanc(DICOM DIMSE) | `ORTHANC`(既定) | `localhost`(VMから見ると`192.168.93.1`) | `4242`(既定104ではない点に注意) | - |
| Orthanc(REST API) | - | `localhost` | - | `8042` |
| DVTk Storage SCU/SCP Emulator | `DVTK_STR_SCU`/`DVTK_STR_SCP`(既定) | - | `104`(既定、要変更) | - |

`dicom-tool-3`はDICOMweb(QIDO-RS/WADO-RS/STOW-RS)を実装していません。HTTP側のAPIは
GraphQL(`http://<host>:5030/graphql`)であり、DICOM標準のHTTPプロトコルではない点に注意
してください(`docs/CONTRACT.md`参照)。

## 重要な注意点: VM⇔ホストPC間の通信にはファイアウォール許可が必要

VM上のdicom-tool-3から、このホストPC上で動くツール(Orthanc等)へC-MOVEやC-STOREで
到達させたい場合、**ホストPC側のWindowsファイアウォールが受信をブロックしていることが
多い**(実際に発生・解決済み。C-FINDは通るのにC-MOVEだけ`Peer aborted Association`で
失敗する、という形で気づいた)。管理者権限のPowerShellで、対象ポートに対してVMのサブネット
からの受信を許可する必要があります。

```powershell
New-NetFirewallRule -DisplayName "<分かりやすい名前>" -Direction Inbound -Protocol TCP -LocalPort <ポート番号> -RemoteAddress 192.168.93.0/24 -Action Allow
```

**この会社の環境でも同じ構図(VM上のPACS/DICOMサービスから、ホストPC上の別のDICOMツールへの
通信)が再発する可能性が高いので、通信が失敗した時はまずこれを疑うこと。** 詳細は
[dcmtk.md「よくあるハマりポイント」](./dcmtk.md#よくあるハマりポイントvmホストpc間の通信)参照。
