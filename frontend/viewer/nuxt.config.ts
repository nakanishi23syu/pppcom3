// ======================================================
// nuxt.config.ts — Viewerアプリの設定ファイル
// ======================================================
// dicom-tool-2/nuxt（ワークリスト機能とビューア機能が同居したモノリシックなNuxtアプリ）から
// 「画像表示」に関する部分だけを切り出した独立サービス。
//
// 【なぜワークリストとビューアを別プロセスに分離したか】
// frontend/worklist/nuxt.config.ts にも同じ説明を書いているが要点だけ再掲する:
// ビューアは医療機器ソフトウェアとしての規制対応もあり改修頻度が低い一方、ワークリストは
// 業務要件変更で頻繁に手を入れる。この非対称性から、実務では別チーム・別リリースサイクルで
// 運用されることが多いため、学習用にもポート（Worklist=3100 / Viewer=3200、
// docs/CONTRACT.md参照）ごと完全に独立したNuxtプロジェクトへ分割した。
// ビューア自体は改修頻度が低い前提のため、移植元の実装をほぼそのままコピーしている
// （DICOM/Temporalサービスほどの重厚な新規解説コメントは付けていない）。
//
// 【サービス間のURL参照について】
// - Viewer(このアプリ) → Backend API(5030): GraphQLで検査・シリーズ・画像データを取得する
//   （utils/graphqlClient.ts が runtimeConfig.public.graphqlEndpoint を参照。Worklistと同じ
//   Backend APIを見る）。認証はhttpOnly Cookie経由（Cookieはブラウザ内でBackend APIの
//   ホストに紐づくため、Worklistでログイン済みならViewerを直接開いても同じログイン状態で
//   GraphQL呼び出しが認証される。詳しくは pages/[seriesInstanceUID].vue のコメント参照）。
// - Worklist(3100) → Viewer(このアプリ): シリーズをダブルクリックした際、Worklist側が
//   `http://localhost:3200/{seriesInstanceUID}` を新しいタブで開く（外部リンク遷移。
//   Viewer側では特別な対応は不要で、直接URLアクセスされても動くようにするだけでよい）。
// - Timeline(5230, Blazor) → Viewer(このアプリ): Timeline側がwindow.open等で
//   このアプリのURLを直接開く想定（Timeline側の実装範囲。Viewer側の対応は不要）。

export default defineNuxtConfig({
  compatibilityDate: '2025-07-15',

  devtools: { enabled: true },

  modules: ['@pinia/nuxt'],

  // ── コンポーネントのauto-import設定 ─────────────────
  // Worklistアプリとは逆に、画像ビューア関連のコンポーネントだけを登録する。
  // components/common（BaseButton等）や features/study（検査一覧関連）はViewerでは使わないため含めない。
  components: [{ path: '~/features/image-viewer/components', pathPrefix: false, global: true }],

  // ── 開発サーバー ────────────────────────────────────
  // docs/CONTRACT.md 1章 / shared/DicomTool.Shared/Constants/ServicePorts.cs の
  // ViewerNuxt = 3200 と一致させる（値そのものの唯一の正はServicePorts.cs側）。
  devServer: {
    port: 3200,
  },

  app: {
    head: {
      title: 'DICOM Tool - Viewer',
      meta: [{ name: 'description', content: 'DICOM学習用ツール - 画像ビューア' }],
    },
  },

  css: ['~/assets/styles/theme.css', '~/assets/styles/main.css'],

  // ── ランタイム設定 ──────────────────────────────────
  // graphqlEndpoint: Backend API(5030)のGraphQLエンドポイント。Worklistと同じBackend APIを
  //   参照する（既定値は開発時のBackend API。環境変数 NUXT_PUBLIC_GRAPHQL_ENDPOINT で上書き可能、
  //   Worklist側と同じ既存パターンを踏襲）。
  runtimeConfig: {
    public: {
      graphqlEndpoint: 'http://localhost:5030/graphql',
    },
  },

  typescript: {
    typeCheck: true,
  },
})
