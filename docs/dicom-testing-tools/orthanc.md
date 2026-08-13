# Orthanc RESTful API リファレンス

Windowsサービス(`Orthanc`)として常駐しており、PC起動時に自動的に立ち上がっている。

- REST API: `http://localhost:8042`
- DICOM(DIMSE)通信: AEタイトル`ORTHANC`、ポート`4242`(既定の104ではない点に注意)
- 認証: このPCの既定設定では、`localhost`からのアクセスに追加のログイン等は不要
  (`/system`等をそのまま叩けることを確認済み)。

Orthanc自体がDICOMのSCU/SCP/フルスペックPACSとして振る舞えるため、**DCMTKのコマンドを
使わなくても、REST APIだけでC-ECHO・C-STORE・C-FIND・C-MOVE相当の操作がすべてできる。**
以下はすべて実際に動作確認済みのコマンド(PowerShell、`Invoke-RestMethod`使用)。

> dicom-tool-3自身も2026-08-13にC-FIND/C-MOVEのSCP実装を追加したため、DCMTKの`findscu`/
> `movescu`を使えばOrthancを介さず直接dicom-tool-3を相手にネイティブなDICOM C-FIND/C-MOVEも
> 試せるようになっている。REST API経由での操作を試したい場合はこのファイル、DCMTKでの
> ネイティブDICOM通信を試したい場合は[dcmtk.md](./dcmtk.md)を参照。

## 0. まず知っておくこと: モダリティ登録が必須

Orthancは、通信したい相手先(送信先・検索先)を**事前に「モダリティ」として登録**しておく
必要がある。DCMTKのようにコマンドライン引数でホスト/ポート/AEを毎回直接指定する方式とは
考え方が違う点に注意。

**モダリティを登録する:**

```powershell
$body = @{ AET = "DICOMTOOL3"; Host = "192.168.93.128"; Port = 11112; Manufacturer = "Generic" } | ConvertTo-Json
Invoke-RestMethod -Uri "http://localhost:8042/modalities/dicomtool3" -Method Put -Body $body -ContentType "application/json"
```

`{名前}`の部分(上の例では`dicomtool3`)はOrthanc内部だけで使う任意のエイリアスで、実際の
AEタイトルは`AET`フィールドで指定する。

**登録済み一覧を確認する:**

```powershell
Invoke-RestMethod -Uri "http://localhost:8042/modalities?expand"
```

**重要(ハマりポイント、実際に遭遇・解決済み):** 外部ツール(DCMTK等)からOrthancへC-FIND/
C-STORE/C-MOVEを送る場合、**呼び出し元が名乗るAEタイトルもOrthancのモダリティ一覧に
登録されていないと拒否される。** 例えばDCMTKの`findscu`を`-aet DICOMTOOL3SCU`で呼ぶ
なら、`DICOMTOOL3SCU`という名前でも(ダミーのホスト/ポートでよいので)登録しておくこと。

```powershell
$body = @{ AET = "DICOMTOOL3SCU"; Host = "127.0.0.1"; Port = 1; Manufacturer = "Generic" } | ConvertTo-Json
Invoke-RestMethod -Uri "http://localhost:8042/modalities/dicomtool3scu" -Method Put -Body $body -ContentType "application/json"
```

## 1. C-ECHO相当(疎通確認)

```powershell
Invoke-RestMethod -Uri "http://localhost:8042/modalities/dicomtool3/echo" -Method Post -Body "{}" -ContentType "application/json"
```

動作確認済み(dicom-tool-3 VM宛て)。成功時は空のJSON(`{}`)が返る。

## 2. Orthanc自身にDICOMファイルを取り込む(C-STOREの送信元データを用意する)

REST APIでC-STORE/C-FINDを試すには、まずOrthanc自身の中にデータが必要。生の`.dcm`ファイルを
そのままPOSTするだけでよい。

```powershell
$bytes = [System.IO.File]::ReadAllBytes("services\DicomTool.DicomScp\SampleData\sample1.dcm")
Invoke-RestMethod -Uri "http://localhost:8042/instances" -Method Post -Body $bytes -ContentType "application/dicom"
```

動作確認済み。レスポンスの`ParentStudy`がそのファイルのStudyのOrthanc内部ID(以降`studyId`)。

## 3. C-STORE相当(Orthancが持っているデータを外部へ送信する)

```powershell
Invoke-RestMethod -Uri "http://localhost:8042/modalities/dicomtool3/store" -Method Post -Body ('"' + $studyId + '"') -ContentType "application/json"
```

動作確認済み(dicom-tool-3 VM宛て)。`studyId`の代わりにシリーズID・インスタンスIDを渡せば、
その単位だけ送信することもできる。

## 4. C-FIND相当(外部へ検索をかける)

```powershell
$queryBody = @{ Level = "Study"; Query = @{ PatientID = "*" } } | ConvertTo-Json
$q = Invoke-RestMethod -Uri "http://localhost:8042/modalities/<登録名>/query" -Method Post -Body $queryBody -ContentType "application/json"
# $q.Path が /queries/{id} の形で返る

# 結果一覧(インデックス番号のリスト)
Invoke-RestMethod -Uri "http://localhost:8042$($q.Path)/answers"

# 個々の結果の中身(タグ値)を見る
Invoke-RestMethod -Uri "http://localhost:8042$($q.Path)/answers/0/content?simplify"
```

動作確認済み(自分自身`ORTHANC`宛てのループバック検索で確認。他の登録済みモダリティに
対しても同じ手順でよい)。`Level`は`Patient`/`Study`/`Series`/`Instance`のいずれか。

## 5. C-MOVE相当(検索結果を指定した宛先へ転送させる)

C-FINDで得た`answers`の各要素に対して「retrieve」を呼ぶと、内部的にDICOMのC-MOVEが
実行される。

```powershell
$body = @{ TargetAet = "DICOMTOOL3" } | ConvertTo-Json
Invoke-RestMethod -Uri "http://localhost:8042$($q.Path)/answers/0/retrieve" -Method Post -Body $body -ContentType "application/json"
```

動作確認済み(自分自身への検索結果を、転送先`TargetAet=DICOMTOOL3`＝dicom-tool-3のVMへ
実際に転送。レスポンスの`DimseErrorStatus: 0`が成功を示す。VM側のGraphQL APIで新規Study
登録も確認済み)。`TargetAet`を省略するとOrthanc自身が受け取る。

## 6. Orthanc自身のデータをRESTだけで検索したい場合(DICOM通信を使わないショートカット)

外部への実際のDICOM C-FINDではなく、「Orthancが今持っているデータの中から探したい」だけ
なら、こちらの方が手軽(DIMSE通信を経由しないため高速)。

```powershell
$body = @{ Level = "Study"; Query = @{ PatientID = "*" } } | ConvertTo-Json
Invoke-RestMethod -Uri "http://localhost:8042/tools/find" -Method Post -Body $body -ContentType "application/json"
```

## テスト結果まとめ(このセッションでの検証結果)

| 操作 | エンドポイント | 相手先 | 結果 |
| --- | --- | --- | --- |
| モダリティ登録 | `PUT /modalities/{name}` | - | 成功 |
| C-ECHO | `POST /modalities/{name}/echo` | 自分自身(ORTHANC)/dicom-tool-3 VM | どちらも成功 |
| ファイル取込 | `POST /instances` | - | 成功 |
| C-STORE | `POST /modalities/{name}/store` | 自分自身/dicom-tool-3 VM | どちらも成功 |
| C-FIND | `POST /modalities/{name}/query` | 自分自身(ループバック) | 成功 |
| C-MOVE | `POST /queries/{id}/answers/{n}/retrieve` | 転送先=自分自身/dicom-tool-3 VM | どちらも成功 |

## 参考: Orthancの主要な既定値

| 項目 | 値 |
| --- | --- |
| AEタイトル | `ORTHANC` |
| DICOMポート | `4242` |
| HTTP(REST)ポート | `8042` |
| デフォルトのRetrieve方式 | `C-MOVE`(`/system`のレスポンスの`DicomDefaultRetrieveMethod`で確認済み) |

`/system`エンドポイント(`GET http://localhost:8042/system`)を叩けば、上記含めた現在の
設定値をいつでも確認できる。
