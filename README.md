# dicom-tool-3

`dicom-tool` / `dicom-tool-2` の学習用DICOMツールを土台に、**実務のPACS/院内システムに近いマイクロサービス構成**
（DICOM通信・ワークフローエンジン・常駐アプリ・複数フロントエンド）を1つのリポジトリ（モノレポ）に
再現した学習用プロジェクトです。詳細な経緯は [`実務環境再現_提案書.md`](./実務環境再現_提案書.md) を参照してください。

> **状態**: 全サービス実装済み。各サービスは個別にビルド・実機検証済みです（詳細は
> [`docs/architecture.md`](./docs/architecture.md) 5章の検証状況一覧を参照）。
> 手を動かす前に必ず [`手動セットアップ手順.md`](./手動セットアップ手順.md) を読んでください
> （Docker Desktopの起動確認等、コードでは自動化できない準備作業が書かれています）。

## 全体構成

詳細な契約（ポート番号・AEタイトル・Temporalの識別子・データフロー図）は
**[`docs/CONTRACT.md`](./docs/CONTRACT.md) が唯一の正**です。実装で疑問があれば必ずそちらを参照してください。

| サービス | 技術 | ディレクトリ | ポート |
|---|---|---|---|
| Backend API | C# / GraphQL(HotChocolate) / EF Core | `backend/DicomTool.Api` | 5030 |
| DICOM SCU/SCP | C# / fo-dicom | `services/DicomTool.DicomScp` | 11112 (DIMSE) / 8090 (管理API) |
| Temporal Worker | C# / Temporal SDK | `services/DicomTool.Worker` | - |
| 常駐トレイアプリ | C# / WinForms | `services/DicomTool.TrayApp` | 5299 |
| Worklist | Nuxt3 | `frontend/worklist` | 3100 |
| Viewer | Nuxt3 | `frontend/viewer` | 3200 |
| Timeline | Blazor WebAssembly | `frontend/timeline/DicomTool.Timeline` | 5230 |
| 共有ライブラリ | C# class library | `shared/DicomTool.Shared` | - |

学習ドキュメント:
- [`docs/dicom-protocol-guide.md`](./docs/dicom-protocol-guide.md) — DICOM通信(DIMSE/C-ECHO/C-STORE/AEタイトル)の解説
- [`docs/temporal-workflow-guide.md`](./docs/temporal-workflow-guide.md) — Temporalワークフローの解説
- [`docs/architecture.md`](./docs/architecture.md) — 全体アーキテクチャ図

## クイックスタート

```bash
# 1. インフラ（PostgreSQL + Temporal）を起動
docker compose up -d

# 2. Backend API（初回起動時にマイグレーション適用）
cd backend/DicomTool.Api && dotnet run

# 3. Temporal Worker
cd services/DicomTool.Worker && dotnet run

# 4. DICOM SCU/SCP
cd services/DicomTool.DicomScp && dotnet run

# 5. 常駐トレイアプリ（Windows専用）
cd services/DicomTool.TrayApp && dotnet run

# 6. フロントエンド3つ（別ターミナルでそれぞれ）
cd frontend/worklist && npm install && npm run dev
cd frontend/viewer && npm install && npm run dev
cd frontend/timeline/DicomTool.Timeline && dotnet run
```

詳しい手順・前提ソフトのインストールは [`手動セットアップ手順.md`](./手動セットアップ手順.md) を参照。
実務相当のVM(Windows Server 2025 + IIS)環境をゼロから構築したい場合は
[`VM構築手順.md`](./VM構築手順.md) を参照（ローカルのdocker-compose環境は使わず、VM上に
PostgreSQL・IIS・各サービスを直接構築する手順）。
