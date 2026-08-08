// ======================================================
// nuxt.config.ts — Worklistアプリの設定ファイル
// ======================================================
// このプロジェクトは dicom-tool-2/nuxt（ワークリスト機能とビューア機能が同居した
// モノリシックなNuxtアプリ）から「検査一覧・アップロード・ログイン」に関する部分だけを
// 切り出した独立サービスである。
//
// 【なぜワークリストとビューアを別プロセスに分離したか】
// 実務のPACS的システムでは、「検査一覧（読影ワークリスト）」と「画像表示（ビューア）」は
// 別々のチームが開発・別々のリリースサイクルで運用することが多い（ビューアは医療機器
// ソフトウェアとしての規制対応もあり改修頻度が低く、ワークリストは業務要件変更で
// 頻繁に手を入れる、という非対称性があるため）。1つのNuxtアプリに同居させたままだと
// 「ビューアの些細な修正のためにワークリスト全体を再ビルド・再デプロイする」羽目になり、
// 障害影響範囲も無用に広がる。dicom-tool-3ではこれを学習するため、ポートも別
// （Worklist=3100 / Viewer=3200、docs/CONTRACT.md参照）の完全に独立したNuxtプロジェクトへ分割した。
//
// 【サービス間のURL参照について】
// - Worklist(このアプリ) → Backend API(5030): GraphQLで検査データを取得・更新する
//   （utils/graphqlClient.ts が runtimeConfig.public.graphqlEndpoint を参照）。
// - Worklist → Viewer(3200): シリーズをダブルクリックした際、Viewer側の
//   `http://localhost:3200/{seriesInstanceUID}` へ別タブ/別ページとして遷移する
//   （Viewerは別プロセスのため、Nuxtのpages遷移ではなく通常のURL遷移になる）。
// - Worklist → TrayApp(5299): 検査を右クリックした際のコンテキストメニュー
//   「タイムラインを開く」から、常駐トレイアプリのローカルHTTP APIへPOSTする
//   （utils/trayAppService.ts が runtimeConfig.public.trayAppEndpoint を参照）。
//   TrayAppがOSネイティブの機能でTimeline(5230)をブラウザで開く（詳細はutils/trayAppService.ts参照）。

export default defineNuxtConfig({
  compatibilityDate: '2025-07-15',

  devtools: { enabled: true },

  // ── モジュール ──────────────────────────────────────
  modules: ['@pinia/nuxt'],

  // ── コンポーネントのauto-import設定 ─────────────────
  // 元のdicom-tool-2/nuxtでは features/image-viewer/components もここに登録していたが、
  // Worklistアプリにはビューア機能を含めないため除外している（Viewer側のnuxt.config.tsを参照）。
  components: [
    { path: '~/components/common', pathPrefix: false, global: true },
    { path: '~/features/study/components', pathPrefix: false, global: true },
  ],

  // ── 開発サーバー ────────────────────────────────────
  // docs/CONTRACT.md 1章 / shared/DicomTool.Shared/Constants/ServicePorts.cs の
  // WorklistNuxt = 3100 と一致させる（値そのものの唯一の正はServicePorts.cs側）。
  devServer: {
    port: 3100,
  },

  // ── アプリ全体のhead設定 ────────────────────────────
  app: {
    head: {
      title: 'DICOM Tool - Worklist',
      meta: [{ name: 'description', content: 'DICOM学習用ツール - 検査ワークリスト' }],
    },
  },

  // ── CSS ─────────────────────────────────────────────
  css: ['~/assets/styles/theme.css', '~/assets/styles/main.css'],

  // ── ランタイム設定 ──────────────────────────────────
  // graphqlEndpoint: Backend API(5030)のGraphQLエンドポイント。
  //   環境変数 NUXT_PUBLIC_GRAPHQL_ENDPOINT で上書き可能（既定値は開発時のBackend API）。
  // trayAppEndpoint: 常駐トレイアプリ(5299)のローカルHTTP APIのベースURL（新設）。
  //   環境変数 NUXT_PUBLIC_TRAY_APP_ENDPOINT で上書き可能。
  // viewerEndpoint: Viewerアプリ(3200)のベースURL。シリーズをダブルクリックした際、
  //   このアプリ内のNuxtルートではなく別プロセスのViewerアプリへ外部リンクとして
  //   遷移する必要があるため追加した（詳しくは pages/index.vue の openSeries() を参照）。
  //   環境変数 NUXT_PUBLIC_VIEWER_ENDPOINT で上書き可能。
  runtimeConfig: {
    public: {
      graphqlEndpoint: 'http://localhost:5030/graphql',
      trayAppEndpoint: 'http://localhost:5299',
      viewerEndpoint: 'http://localhost:3200',
    },
  },

  // ── TypeScript ───────────────────────────────────────
  typescript: {
    typeCheck: true,
  },
})
