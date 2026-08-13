# DCMTK コマンドリファレンス

Chocolateyでインストール済み(`choco install dcmtk`)。実行ファイルは
`C:\ProgramData\chocolatey\bin\`にPATH登録済みなので、どこからでもコマンド名だけで実行できる。

> 前提: [`README.md`](./README.md)の「既定値一覧」表を先に読むこと。
> **dicom-tool-3は現在、C-ECHO/C-STORE/C-FIND/C-MOVEのすべてに対応済み**
> (2026-08-13、`services/DicomTool.DicomScp/Services/DicomScpService.cs`にC-FIND/C-MOVEの
> SCP実装を追加済み。対応前の制限事項だった内容はこのファイルの下部「過去の制限事項
> （解消済み）」に記録として残してある)。

## C-ECHO(疎通確認、いわゆるDICOM Ping)

```text
echoscu -v -aet <呼び出し元AE> -aec <相手先AE> <host> <port>
```

**動作確認済み(dicom-tool-3 VM宛て):**

```text
echoscu -v -aet DICOMTOOL3SCU -aec DICOMTOOL3 192.168.93.128 11112
```

結果: `Received Echo Response (Success)`

## C-STORE(画像送信)

```text
storescu -v -aet <呼び出し元AE> -aec <相手先AE> <host> <port> <ファイル.dcm>
```

**動作確認済み(dicom-tool-3 VM宛て):**

```text
storescu -v -aet DICOMTOOL3SCU -aec DICOMTOOL3 192.168.93.128 11112 sample2.dcm
```

結果: `Received Store Response (Success)`。送信後、dicom-tool-3のTemporalワークフローが
起動し、GraphQL API(`http://192.168.93.128:5030/graphql`)の`studies`クエリで新規レコード
登録が確認できる。

フォルダ内のファイルをまとめて送りたい場合は、末尾にファイルの代わりにディレクトリを指定し
`--scan-directories`(`+sd`)・`--recurse`(`+r`)を付ける(DCMTK公式オプション、本セッションでは
未検証・参考情報)。

## C-FIND(検索)

```text
findscu -v -S -aet <呼び出し元AE> -aec <相手先AE> -k "0008,0052=<Level>" -k "<検索したいタグ>=" <host> <port>
```

- `-S` は「Study Root Query/Retrieve Information Model」を明示的に使うオプション。
  **これを付けないとOrthanc/dicom-tool-3どちらでも`No Acceptable Presentation Contexts`で
  拒否されることを確認済み**（既定のPatient Rootモデルは両者ともサポートしていないため、
  基本`-S`を付けるのが無難）。
- `-k "タグ=値"` で検索条件、`-k "タグ="` (値を空にする)で「その項目を結果に含めて返してほしい」
  という意味になる(DICOM C-FINDの作法)。値には`*`(任意文字列)・`?`(任意1文字)のワイルドカードが
  使える。
- サポートする階層(`0008,0052`の値): `STUDY`・`SERIES`のみ(`PATIENT`・`IMAGE`は
  dicom-tool-3では非対応。0件ヒット扱いでSuccess応答が返る)。

**動作確認済み(dicom-tool-3 VM宛て、STUDY階層、全件取得):**

```text
findscu -v -S -aet DICOMTOOL3SCU -aec DICOMTOOL3 -k "0008,0052=STUDY" -k "0010,0010=" -k "0010,0020=" -k "0020,000d=" -k "0008,0050=" 192.168.93.128 11112
```

結果: `Received Final Find Response (Success)`。登録済みの全Study(PatientID・PatientName・
StudyInstanceUID・AccessionNumber・StudyDate・ModalitiesInStudy・
NumberOfStudyRelated{Series,Instances})がPending応答として1件ずつ返る。

**動作確認済み(ワイルドカード検索、PatientIDの前方一致):**

```text
findscu -v -S -aet DICOMTOOL3SCU -aec DICOMTOOL3 -k "0008,0052=STUDY" -k "0010,0020=patient-10*" -k "0020,000d=" 192.168.93.128 11112
```

`patient-101`・`patient-103`のみヒットし`patient-pytest`は除外されることを確認済み
(SQLの`LIKE`に変換して絞り込んでいる。`services/DicomTool.DicomScp/Services/
DicomQueryService.cs`参照)。

**動作確認済み(SERIES階層、StudyInstanceUIDで絞り込み):**

```text
findscu -v -S -aet DICOMTOOL3SCU -aec DICOMTOOL3 -k "0008,0052=SERIES" -k "0020,000d=<StudyInstanceUID>" -k "0020,000e=" -k "0008,103e=" 192.168.93.128 11112
```

**Orthanc宛ての例(こちらも従来通り成功する):**

```text
findscu -v -S -aet DICOMTOOL3SCU -aec ORTHANC -k "0008,0052=STUDY" -k "0010,0010=" -k "0010,0020=" -k "0020,000d=" localhost 4242
```

**重要な注意点(ハマりポイント、実際に遭遇・解決済み):** Orthanc側は、**呼び出し元のAEタイトル
(`-aet`で指定した値)がOrthancのモダリティ一覧に事前登録されていないと、アソシエーションは
受け付けてもDIMSE要求自体を`Peer aborted Association`で拒否する。** 存在しないダミーの
ホスト/ポートでもよいので、まずOrthanc側に自分のAEタイトルを登録しておくこと
（登録方法は[orthanc.md](./orthanc.md)参照）。dicom-tool-3宛てにはこの事前登録は不要
(呼び出し元AEのチェックをしていないため)。

## C-MOVE(検索結果を指定した宛先へ転送させる)

```text
movescu -v -S -aet <呼び出し元AE> -aec <検索先AE(C-MOVEを受けるSCP)> --move <転送先AE> -k "0008,0052=<Level>" -k "<検索条件タグ>=<値>" <host> <port>
```

- `--move <AE>` に指定するのは「画像の転送先」のAEタイトル。**movescuを実行しているマシン
  自身が受信するわけではない**。C-MOVEを受けたSCPが、自分が持つ「AEタイトル→host:port」の
  対応表を調べて、そこへ自分からC-STOREを送る、という仕組み。
- dicom-tool-3側のこの対応表は、`appsettings.json`(共通)/`appsettings.Development.json`
  (開発用)/`appsettings.Production.json`(VM上、Git管理外)の`RemoteAeTitles`セクションで
  設定する(`services/DicomTool.DicomScp/Services/RemoteAeRegistry.cs`)。**登録の無い
  AEタイトルへC-MOVEしようとすると`Refused: MoveDestinationUnknown`で失敗する。**
  新しい転送先を追加したい時はここに追記してサービスを再起動する。
- **転送先AEタイトルは実際の宛先システムが受け付けるAEタイトルと一致させること。**
  自分自身(dicom-tool-3)を転送先にする場合、`RemoteAeTitles`のキーは
  `DicomNetworkConstants.OwnAeTitle`(=`DICOMTOOL3`)と一致させる必要がある
  （`DicomScpService.OnReceiveAssociationRequestAsync`がCalled AE Titleを検証しているため、
  違う名前のエイリアスにすると転送先での接続自体が拒否される。実際に`SELFLOOP`という
  独自エイリアス名で試して失敗し、`DICOMTOOL3`に直して成功したことを確認済み）。
- DICOMのAEタイトルは**最大16文字**という制限がある。18文字の名前
  (`DICOMTOOL3SELFTEST`)を試したところ、送信時に暗黙的に切り詰められて登録名と
  一致しなくなり`MoveDestinationUnknown`になる、という事象も実際に確認済み。短い名前を使うこと。

### 動作確認済み1: 自己ループ(dicom-tool-3自身が検索元かつ転送先)

```text
movescu -v -S -aet DICOMTOOL3SCU -aec DICOMTOOL3 --move DICOMTOOL3 -k "0008,0052=STUDY" -k "0020,000d=<StudyInstanceUID>" 192.168.93.128 11112
```

結果: `Received Final Move Response (Success)`。ファイアウォールに影響されない、
実装そのものの動作確認に最適(VM内で完結するため)。

### 動作確認済み2: dicom-tool-3からOrthancへ転送

```text
movescu -v -S -aet DICOMTOOL3SCU -aec DICOMTOOL3 --move ORTHANC -k "0008,0052=STUDY" -k "0020,000d=<StudyInstanceUID>" 192.168.93.128 11112
```

結果: `Received Final Move Response (Success)`。直後にOrthancのREST API
(`GET http://localhost:8042/patients`)で対象患者が新規登録されていることを確認済み。

### 動作確認済み3(従来からの逆方向): Orthancからdicom-tool-3へ転送

```text
movescu -v -S -aet DICOMTOOL3SCU -aec ORTHANC --move DICOMTOOL3 -k "0008,0052=STUDY" -k "0020,000d=<転送したいStudyInstanceUID>" localhost 4242
```

結果: `Received Final Move Response (Success)`。dicom-tool-3のGraphQL API(`studies`クエリ)
で新規登録を確認済み。

### よくあるハマりポイント(VM⇔ホストPC間の通信)

C-MOVEの転送先がVMの外(このホストPC上のOrthanc等)の場合、**ホストPC側のWindows
ファイアウォールが、VMからの受信接続をデフォルトでブロックしている**ことがある
(実際にこれで`movescu`が`Peer aborted Association`で失敗し、原因調査に時間がかかった)。
症状としては「C-FINDは通るのにC-MOVEだけ失敗する」「VM側のイベントログに
`SocketException (10060): 接続済みの呼び出し先が一定の時間を過ぎても応答しなかった`が出る」
という形で現れる。管理者権限のPowerShellで、転送先アプリのポートに対して受信許可を追加する
必要がある(例: Orthancのポート4242、VMのサブネットからのみ許可):

```powershell
New-NetFirewallRule -DisplayName "Orthanc DICOM (4242) from VM" -Direction Inbound -Protocol TCP -LocalPort 4242 -RemoteAddress 192.168.93.0/24 -Action Allow
```

**会社の環境でも同じ構図(VM上のPACS/DICOMサービス → ホストPC上で動く別のDICOMツールへの
C-MOVE)を試す時は、まずこのファイアウォール設定を疑うこと。** VM側のアプリのバグではなく、
ホストOS側の受信ブロックが原因であることが多い。

## その他の補助ツール

### `dcmdump` ― DICOMファイルの中身をタグ一覧で表示する

```text
dcmdump <ファイル.dcm>
```

動作確認済み。ファイルメタ情報(SOPClassUID等)からデータセット本体まで、タグ番号・VR・値を
そのまま表示できる。「このファイルに実際どんなタグが入っているか」をざっと見たい時に最速。

### `dcmodify` ― タグの値を書き換える(参考、本セッションでは未検証)

```text
dcmodify -i "(0010,0010)=新しい患者名" <ファイル.dcm>
```

Python(pydicom)で似たようなことをする場合は`tools/dicom_test_data_generator/`のスクリプトも
参照。用途に応じて使い分けるとよい(DCMTKはCLIでサッと1タグ直したい時、pydicomスクリプトは
複数ファイルを一括で加工したい時、など)。

## テスト結果まとめ(このセッションでの検証結果)

| コマンド | 相手先 | 結果 |
| --- | --- | --- |
| `echoscu` | dicom-tool-3 VM (11112) | 成功 |
| `storescu` | dicom-tool-3 VM (11112) | 成功 |
| `findscu -S`(STUDY、全件/ワイルドカード) | dicom-tool-3 VM (11112) | 成功 |
| `findscu -S`(SERIES) | dicom-tool-3 VM (11112) | 成功 |
| `findscu -S` | Orthanc (4242) | 成功(要: 呼び出し元AEの事前登録) |
| `movescu -S --move DICOMTOOL3`(自己ループ) | dicom-tool-3 VM (11112) | 成功 |
| `movescu -S --move ORTHANC` | dicom-tool-3 VM (11112) → Orthanc | 成功(要: ホストPC側ファイアウォール許可) |
| `movescu -S --move DICOMTOOL3` | Orthanc (4242) → dicom-tool-3 VM | 成功 |
| `dcmdump` | ローカルファイル | 成功 |

## 過去の制限事項(解消済み)

以前(2026-08-13の実装追加前)は、dicom-tool-3の`DicomTool.DicomScp`はC-ECHO/C-STOREの
SCPしか実装しておらず、C-FIND/C-MOVEは`Peer aborted Association`で必ず失敗していた。
「dicom-tool-3自身は学習用に意図的に機能を絞ってあるので、C-FIND/C-MOVEを試したければ
Orthancを対象にする」という運用でカバーしていたが、その後C-FIND/C-MOVEのSCP実装を追加した
ため、現在はdicom-tool-3自身を相手にしたテストもすべて可能になっている。
