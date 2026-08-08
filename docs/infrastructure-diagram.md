# インフラ構成図（PlantUML）

> このドキュメントは、実際にVM上にどうデプロイされているか（[`VM構築手順.md`](../VM構築手順.md)
> 24章のファイアウォール一覧が一次情報源）と、ポート番号の唯一の正である
> [`shared/DicomTool.Shared/Constants/ServicePorts.cs`](../shared/DicomTool.Shared/Constants/ServicePorts.cs)
> を元に、現在のインフラ構成をPlantUML図として可視化したものです。
>
> [`docs/architecture.md`](./architecture.md)の構成図（Mermaid、リクエストフロー中心）と
> 役割が重ならないよう、こちらは「実際にどのマシン(ホストPC / VM)の上に何が乗っていて、
> どのポートで通信しているか」という**物理配置**に焦点を当てています。処理の流れの詳細
> （なぜTemporalを挟むか等）は`docs/architecture.md`・`docs/CONTRACT.md`を参照してください。

---

## 図の見方

- ホストPC（あなたが普段使っているWindows PC）と、VMware上の仮想マシン`dicom-pacs-vm`を
  それぞれ大きな箱として分けています。
- 両者は`192.168.93.0/24`というVMware NATの仮想ネットワークの中でIPアドレスを持ち、
  相互に通信できます（ホストPCから見たVMのIPが`192.168.93.128`、`VM構築手順.md` 0章・9章参照）。
- 矢印のラベルは「何のプロトコルで」「何のために」通信するかを表しています。

```plantuml
@startuml dicom-tool-3_infrastructure
title dicom-tool-3 インフラ構成図（VM構築手順.md 24章 / ServicePorts.cs 準拠）

skinparam componentStyle rectangle
skinparam wrapWidth 200
skinparam defaultTextAlignment center

left to right direction

package "ホストPC (Windows)" as HostPC {
  actor "利用者\n(ブラウザ操作)" as User
  component "ブラウザ" as Browser
  component "DicomTool.TrayApp\n:5299 (localhost限定)" as TrayApp
}

package "VMware NAT ネットワーク\n192.168.93.0/24" as NatNetwork {

  package "VM: dicom-pacs-vm\n192.168.93.128" as Vm {

    package "IIS (Windowsの標準Webサーバー)" as Iis {
      component "DicomTool.Api\n(GraphQL)\n:5030" as Api
      component "Timeline\n(Blazor WASM 静的サイト)\n:5230" as Timeline
    }

    package "NSSM管理下の常駐サービス" as NssmServices {
      component "DicomToolScp\n(DicomTool.DicomScp)\nDIMSE:11112 / 管理API:8090" as Scp
      component "DicomToolWorker\n(DicomTool.Worker)\n(待受ポートなし)" as Worker
      component "TemporalServer\ngRPC:7233 / WebUI:8233" as Temporal
      component "DicomToolWorklist\n(Node/Nuxt3)\n:3100" as Worklist
      component "DicomToolViewer\n(Node/Nuxt3)\n:3200" as Viewer
    }

    database "PostgreSQL\n:5432" as Postgres
  }

  component "外部モダリティ\n(CT/MRI等) / 自己SCU" as Modality
}

' ---- ユーザーの操作 ----
User --> Browser : 操作
Browser --> Worklist : HTTP\n(検査一覧・アップロード)
Browser --> Viewer : HTTP\n(画像表示、別タブ)
Browser --> Timeline : HTTP\n(タイムライン表示)
Browser --> TrayApp : HTTP POST\n(右クリック→起動命令)
TrayApp --> Browser : 既定ブラウザで開く\n(Timelineを新規表示)

' ---- フロントエンド → バックエンド ----
Worklist --> Api : GraphQL / HTTP
Viewer --> Api : GraphQL / HTTP
Timeline --> Api : GraphQL / HTTP

' ---- バックエンド内部 ----
Api --> Postgres : SQL (EF Core, Migrate含む)
Api --> Temporal : gRPC\n(UploadDicomWorkflow等の起動)

Worker --> Temporal : gRPC\n(タスクをポーリング)
Worker --> Postgres : SQL (EF Core)

Scp --> Temporal : gRPC\n(UploadDicomWorkflow起動依頼)

' ---- DICOM通信 ----
Modality --> Scp : DIMSE/TCP :11112\n(C-ECHO / C-STORE)

' ---- IISのリバースプロキシ的位置づけ ----
note right of Iis
  Backend API・TimelineはIISが直接ホスト。
  Worklist/Viewer(Node.js)へは、必要に応じて
  IISのARR(Application Request Routing)経由の
  リバースプロキシ構成も取れる(VM構築手順.md 22章)。
end note

note bottom of Vm
  Dockerは使わずネイティブインストール
  (PostgreSQL / Temporal / Node.js / .NET Hosting Bundle)。
  常駐トレイアプリ(TrayApp)はVMには置かない
  (利用者のホストPC側にインストールするアプリのため)。
end note

@enduml
```

---

## ポート番号の対応表（再掲・出典明記）

このドキュメントに書いたポート番号はすべて次の2つの一次情報源と一致させています。差異が
生じた場合はこの2つのファイルが正であり、本ドキュメントの図を修正してください。

| コンポーネント | ポート | 出典 |
|---|---|---|
| DicomTool.TrayApp | 5299 | `ServicePorts.TrayAppHttp` |
| DicomTool.Api (GraphQL) | 5030 | `ServicePorts.BackendApiHttp` |
| Timeline (Blazor WASM) | 5230 | `ServicePorts.TimelineBlazor` |
| DicomToolScp DIMSE | 11112 | `ServicePorts.DicomScpDimse` |
| DicomToolScp 管理API | 8090 | `ServicePorts.DicomScpManagementHttp` |
| TemporalServer gRPC | 7233 | `ServicePorts.TemporalFrontendGrpc` |
| TemporalServer Web UI | 8233 | `ServicePorts.TemporalWebUi` |
| DicomToolWorklist (Nuxt) | 3100 | `ServicePorts.WorklistNuxt` |
| DicomToolViewer (Nuxt) | 3200 | `ServicePorts.ViewerNuxt` |
| PostgreSQL | 5432 | `ServicePorts.PostgresHost` |

`VM構築手順.md` 24章のファイアウォール受信規則一覧も、上記と同じポート番号で構成されています
（DicomToolWorkerのみ待受ポートを持たないため、ファイアウォール規則の対象外です）。
