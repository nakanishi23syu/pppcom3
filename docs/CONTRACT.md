# サービス間契約（CONTRACT）

> このドキュメントは、dicom-tool-3 を構成する複数サービス（別プロセス・別リポジトリではなく
> 「別プロジェクト」として1つのリポジトリに同居する、いわゆる **モノレポ構成のマイクロサービス**）が
> 互いに依存する「決め事」を一箇所にまとめたものです。
> 各サービスの実装者（人間・エージェント問わず）は、ポート番号やキュー名を独自に決めず、
> 必ずこのドキュメントと `shared/DicomTool.Shared` の定数を参照してください。

## 1. サービス一覧とポート

| サービス | 技術 | ディレクトリ | ポート | 役割 |
|---|---|---|---|---|
| Backend API | C# / HotChocolate(GraphQL) / EF Core | `backend/DicomTool.Api` | **5030** | 検査・シリーズ・画像のCRUD、認証、`uploadDicomFiles` Mutation（旧経路） |
| DICOM SCU/SCP | C# / fo-dicom | `services/DicomTool.DicomScp` | **11112**(DIMSE) / **8090**(管理REST+Swagger) | C-ECHO/C-STORE受信（新経路）、C-FIND/C-MOVE応答（検索・転送依頼の受け側）、疎通テスト |
| Temporal Worker | C# / Temporal SDK | `services/DicomTool.Worker` | (待受ポートなし。Temporal Serverに接続しにいく側) | アップロード/削除ワークフローの実行 |
| 常駐トレイアプリ | C# / WinForms + Minimal API | `services/DicomTool.TrayApp` | **5299** | ワークリストからの起動命令をHTTPで受信し、Timelineをブラウザで開く |
| Worklist | Nuxt3 | `frontend/worklist` | **3100** | 検査一覧表示、右クリックでTimeline起動命令を送信 |
| Viewer | Nuxt3 | `frontend/viewer` | **3200** | 画像表示・拡大縮小（独立サービス） |
| Timeline | Blazor WebAssembly | `frontend/timeline` | **5230** | 患者タイムライン表示 |
| PostgreSQL | Docker | `docker-compose.yml` | **5432** | アプリDB(`dicomtool`) + Temporal用DB(`temporal`, `temporal_visibility`)を1インスタンスに同居 |
| Temporal Server | Docker (`temporalio/auto-setup`) | `docker-compose.yml` | **7233**(gRPC frontend) | ワークフローエンジン本体 |
| Temporal Web UI | Docker (`temporalio/ui`) | `docker-compose.yml` | **8233** | ワークフロー実行状況の確認画面 |

これらの値はすべて `shared/DicomTool.Shared/Constants/ServicePorts.cs` に定数として定義する。
C#側の実装は appsettings.json にポート番号をベタ書きせず、可能な範囲でこの定数を参照する
（appsettings自体はASP.NET Coreの制約上ベタ書きせざるを得ないが、コメントでこの定数を指し示すこと）。

## 2. データの流れ（アップロード）― 2つの入口が同じワークフローに合流する

実務PACSでは「モダリティからのDICOM送信」が主入口だが、学習用に「ブラウザからの手動アップロード」も
並行運用する（ユーザー確定事項）。あえて2つの入口を残すのは、**同じ後処理（Temporalワークフロー）に
異なるプロトコルの入口が合流する**という、実務でもよくある構図を学習するため。

```
[経路A] 外部モダリティ/自己SCU ──C-STORE(DIMSE)──> DicomTool.DicomScp (SCP受信)
[経路B] ブラウザ ──HTTP multipart(GraphQL uploadDicomFiles)──> DicomTool.Api

              経路A・経路Bどちらも、受信直後にステージング領域へ生ファイルを書き込むだけ↓
                              (infra/data/dicom-incoming/にファイルを1つ置く)
                                             │
                                             ▼
                    Temporal Client が UploadDicomWorkflow を起動
                    （タスクキュー: dicom-tool-task-queue）
                                             │
                                             ▼
                              DicomTool.Worker がワークフローを実行
                    ① SaveToStorageActivity … fo-dicomでタグ解析 → infra/data/dicom-storage/
                       配下の正規パス {StudyUID}/{SeriesUID}/{SOPUID}.dcm へ移動
                    ② RegisterDicomRecordActivity … PostgreSQLへ Study/Series/Sop をupsert
```

- 「受信」と「ファイル確定保存＋DB登録」を分離するのは、C-STORE応答をアソシエーション内で
  素早く返す必要がある（DICOM側の作法）ことと、ストレージ操作とDB操作それぞれが独立して
  失敗しうる（ディスクフル／DB接続断など）ことをTemporalのリトライで吸収するため。
- ステージング領域とストレージ領域を分けるのは、「受信済みだがまだ正式登録されていない」
  ファイルと、「正式に登録済み」のファイルを混同しないため（＝実務のPACSでも一時受信領域と
  本番アーカイブ領域を分けるのが一般的）。

## 3. データの流れ（削除）

Worklist/Timeline等から削除操作を行う際も、同様に「ストレージ操作」「DB操作」を分離する。

```
DicomTool.Api の deleteStudy/deleteSeries/deleteSop Mutation
        │
        ▼
Temporal Client が DeleteDicomWorkflow を起動
        │
        ▼
DicomTool.Worker が実行
  ① DeleteFromStorageActivity … 対象ファイル/ディレクトリを削除
  ② DeleteRecordActivity      … PostgreSQLから該当レコードを削除（EF CoreのCascadeで子も削除）
```

## 4. Temporalの識別子

| 項目 | 値 | 定義場所 |
|---|---|---|
| Namespace | `default` | `TemporalConstants.Namespace` |
| Task Queue | `dicom-tool-task-queue` | `TemporalConstants.TaskQueue` |
| Upload Workflow Type名 | `UploadDicomWorkflow` | `TemporalConstants.UploadDicomWorkflowTypeName` |
| Delete Workflow Type名 | `DeleteDicomWorkflow` | `TemporalConstants.DeleteDicomWorkflowTypeName` |
| Temporal Server接続先 | `localhost:7233`（開発時） | 各サービスのappsettings `Temporal:Address` |

**設計上のポイント**: Api・DicomScpはWorkflow実装クラスそのものを参照しない
（＝Workerが持つ実装の詳細を知らない）。Temporal .NET SDKの「型なしクライアントAPI」
（`ITemporalClient.StartWorkflowAsync(workflowTypeName, args, options)`）を使い、
Task Queue名とWorkflow Type名という「文字列の約束事」だけで疎結合に呼び出す。
これは他社の外部APIをHTTPで叩くのと似た感覚であり、実務のマイクロサービス感を学ぶ意図がある。

## 5. DICOM通信の識別子

| 項目 | 値 | 定義場所 |
|---|---|---|
| 自システム(SCP)のAEタイトル | `DICOMTOOL3` | `DicomNetworkConstants.OwnAeTitle` |
| 自己疎通テスト用SCUのAEタイトル | `DICOMTOOL3SCU` | `DicomNetworkConstants.TestScuAeTitle` |
| SCP待受ポート | `11112` | `DicomNetworkConstants.ScpPort` |

AEタイトルはDICOM規格上、最大16文字の大文字英数字（規格上は許容範囲がやや緩いが実務慣習として大文字推奨）。
本番のPACS相手と通信する場合は相手先のAEタイトル・IP・ポートを別途確認する必要がある
（`手動セットアップ手順.md` に接続先を切り替える手順を記載）。

### 5-1. C-FIND/C-MOVEの対応範囲

`DicomTool.DicomScp`はC-ECHO/C-STOREに加え、C-FIND（検索）・C-MOVE（転送依頼の受け側）にも
対応する（`Services/DicomScpService.cs`が`IDicomCFindProvider`/`IDicomCMoveProvider`を実装）。
対応する階層はSTUDY・SERIESのみ（PATIENT・IMAGEは0件ヒット扱い）。検索条件の解釈は
`Services/DicomQueryService.cs`が担い、`shared/DicomTool.Shared`のエンティティ
（UserStudy/UserSeries/UserSop）に対してEF Core経由でクエリする。

C-MOVEの転送先（宛先AEタイトル→host:port）は、`appsettings.json`系の`RemoteAeTitles`
セクションに事前登録しておく必要がある（`Services/RemoteAeRegistry.cs`）。未登録のAEタイトルへ
C-MOVEしようとすると`Refused: MoveDestinationUnknown`で失敗する。値は環境（どこにOrthanc等が
あるか）依存のため、ベースの`appsettings.json`には入れず、`appsettings.Development.json`
（ローカル開発用）・`appsettings.Production.json`（VM上、Git管理外）にそれぞれ置く。

実際に検証済みのコマンド・既知の注意点（DICOMのAEタイトルは最大16文字である点、
VM⇔ホストPC間の通信にはWindowsファイアウォールの受信許可が別途必要な点等）は
`docs/dicom-testing-tools/dcmtk.md`にまとめてある。

## 6. ストレージパス規約

各C#サービスは `ContentRootPath`（＝各csprojのディレクトリ）から見て以下の相対パスで
共通のデータ領域を参照する。すべて `backend/`, `services/` 配下の各プロジェクトから見て
深さが2階層（例: `backend/DicomTool.Api`）で揃っているため、相対パスの書式を統一できる。

| 用途 | 相対パス(各サービスのContentRootPathから) | 実体 |
|---|---|---|
| ステージング領域（受信直後・未確定） | `../../infra/data/dicom-incoming/` | `infra/data/dicom-incoming/` |
| 正式ストレージ（Workflow確定後） | `../../infra/data/dicom-storage/` | `infra/data/dicom-storage/` |

この2つのフォルダは `.gitignore` 対象（中身は実行時に生成されるデータのため）。
`infra/data/.gitkeep` 等でフォルダ自体の存在は保持する。

## 7. データベース接続

- 接続先: `Host=localhost;Port=5432;Database=dicomtool;Username=dicomtool;Password=dicomtool_dev_password`
- この資格情報は **学習用ローカルDocker専用の開発用パスワード**であり、本番相当の利用では
  必ず変更すること（`手動セットアップ手順.md` に注記）。
- 接続文字列は各サービスの `appsettings.Development.json` の `ConnectionStrings:Dicom` に置き、
  環境変数 `ConnectionStrings__Dicom` で上書き可能にする（＝実際のVM上のPostgreSQL
  `192.168.93.128` に向き先を変える場合は、この環境変数を上書きするだけで済むように設計する）。
- EF Coreのエンティティ定義・DbContext・Migrationsは `shared/DicomTool.Shared` に置き、
  `backend/DicomTool.Api` と `services/DicomTool.Worker` の両方から参照する
  （2つの別プロセスが同じテーブル定義を「別々に書いて」ズレる事故を防ぐため）。
- 起動時マイグレーション（`db.Database.Migrate()`）は **Backend APIだけ**が行う。
  Workerは「マイグレーション済みである前提」でDbContextを使う（複数プロセスが同時に
  マイグレーションを試みる競合を避けるため）。

## 8. 認証・CORS

- 既存踏襲: JWTをhttpOnly Cookieで発行（`AppConstants.AuthCookieName`）。
- CORS許可オリジンに Worklist(3100)・Viewer(3200)・Timeline(5230) をすべて追加する。

## 9. 旧アップロード経路（GraphQL `uploadDicomFiles`）の扱い

ユーザー確定事項により **並行運用**する。ただし内部実装は「ファイルをステージング領域に書き込み、
Temporalの `UploadDicomWorkflow` を起動する」形に変更し、DICOM C-STORE経路と後処理を共通化する
（4章参照）。従来のように `DicomUploadService` がその場でDB保存まで完結させる実装には戻さない。
