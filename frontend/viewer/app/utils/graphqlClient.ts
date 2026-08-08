// ======================================================
// utils/graphqlClient.ts — GraphQLサーバーへの汎用リクエスト関数
// ======================================================
// Vue版 frontend/src/services/graphqlClient.ts の移植。
//
// 【なぜ app/utils/ に置くか（Nuxtのauto-import）】
// Nuxtは `composables/` と同様に `utils/` 配下のファイルもビルド時にスキャンし、
// エクスポートされている関数・変数を「importなしでどこからでも使えるグローバル」にする
// （auto-import。詳しい仕組みは features/tutorial/components/AutoImportUnit.vue で解説する）。
// 慣習として、Vueのリアクティビティ（ref/computed等）やライフサイクルに依存するものは
// composables/、依存しない純粋なロジック・ユーティリティは utils/ に置く。
// このファイルは「fetchを叩くだけの純粋関数」なので utils/ 側に置いている。
//
// 【GraphQLの呼び方の基本形】
// RESTのように「URLを変えて呼び分ける」のではなく、常に同じエンドポイントへ POST し、
// body に query（GraphQLクエリ言語の文字列）と variables（$変数の実際の値）を JSON で入れて送る。

// GraphQLのレスポンスは常にこの形（{ data, errors }）で返ってくる。
interface GraphQLResponse<T> {
  data?: T
  errors?: { message: string; extensions?: { code?: string } }[]
}

// ======================================================
// GraphQLRequestError — GraphQLのerrors[]をラップしたエラー
// ======================================================
// HotChocolateは認証エラー・認可エラーのどちらも同じメッセージ文言を返すため、
// メッセージだけでは「未ログイン」と「権限不足」を区別できない。
// 代わりに extensions.code（AUTH_NOT_AUTHENTICATED / AUTH_NOT_AUTHORIZED）を
// codes として保持しておき、呼び出し側で判定できるようにする。
export class GraphQLRequestError extends Error {
  constructor(
    message: string,
    public readonly codes: string[]
  ) {
    super(message)
    this.name = 'GraphQLRequestError'
  }
}

// ======================================================
// getGraphqlEndpoint — 実行環境（サーバー/クライアント）を問わずエンドポイントを取得する
// ======================================================
// useRuntimeConfig() はNuxtのcomposableで、nuxt.config.tsのruntimeConfigの値を
// サーバー・クライアントどちらの実行環境でも同じ書き方で取得できる（Vue版の
// constants/env.ts が import.meta.env を集約していたのと同じ役割）。
function getGraphqlEndpoint(): string {
  const config = useRuntimeConfig()
  return config.public.graphqlEndpoint
}

export async function graphqlRequest<T>(
  query: string,
  variables?: Record<string, unknown>
): Promise<T> {
  const headers: Record<string, string> = { 'Content-Type': 'application/json' }

  // ======================================================
  // 【NuxtのSSRとCookie転送についての重要な注意点】
  // ======================================================
  // ブラウザ側で実行されるfetchは `credentials: 'include'` を付けるだけで、
  // ブラウザが自動的にCookieを一緒に送信してくれる（Vue版と同じ挙動）。
  //
  // しかし、このコード自体はサーバー（Nitro）側でも実行され得る
  // （SSR時、および認証ガード用のmiddleware/admin-only.tsがサーバー側で先に評価される
  // ケース）。サーバー同士の通信（Nitro→backend）には「ブラウザ」が存在しないため、
  // credentials: 'include' を付けても何も起きない。ユーザーのブラウザが持っている
  // Cookie（dicom_auth_token）を、Nuxtサーバーが代理でbackendへ手動転送してやる必要がある。
  //
  // useRequestHeaders(['cookie']) はNuxt組み込みのcomposableで、「今処理中のブラウザからの
  // リクエストに付いていたヘッダーの一部」をサーバー実行時にだけ取得できる
  // （クライアント実行時は常に空オブジェクトを返すため、下のスプレッドは無害）。
  // これによって、restoreSession()（plugins/restore-session.ts）がSSR中に呼ばれても、
  // ページの初回HTMLの時点で「ログイン済みかどうか」を正しく判定できるようになる
  // （admin-onlyミドルウェアのようなSSRガードが正しく機能するために必須）。
  const forwardedCookie = import.meta.server ? useRequestHeaders(['cookie']) : {}

  // JWTはbackendがhttpOnly Cookieとして発行するため、フロントエンドはトークンの値を
  // 一切扱わない。credentials: 'include' でCookieをブラウザに自動送受信させる
  // （backend側もCORSでAllowCredentials()を設定済み）。
  const res = await fetch(getGraphqlEndpoint(), {
    method: 'POST',
    headers: { ...headers, ...forwardedCookie },
    credentials: 'include',
    body: JSON.stringify({ query, variables }),
  })

  if (!res.ok) {
    throw new Error(`GraphQLサーバーへの通信に失敗しました (HTTP ${res.status})`)
  }

  const json: GraphQLResponse<T> = await res.json()

  // HTTP自体は成功していても、GraphQL側でエラーが起きていることがある。
  if (json.errors && json.errors.length > 0) {
    const message = json.errors.map((e) => e.message).join(', ')
    const codes = json.errors.map((e) => e.extensions?.code).filter((c): c is string => !!c)
    throw new GraphQLRequestError(message, codes)
  }

  if (json.data === undefined) {
    throw new Error('GraphQLのレスポンスにdataが含まれていません')
  }

  return json.data
}
