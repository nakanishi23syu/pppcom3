// ======================================================
// plugins/restore-session.ts — 起動時にログイン状態を復元するプラグイン
// ======================================================
// Vue版 frontend/src/main.ts の以下の処理に対応する:
//   useAuthStore().restoreSession().catch(() => {}).finally(() => app.mount('#app'))
//
// 【Nuxtのプラグインとは】
// Nuxtアプリが初期化されるタイミングで1回だけ実行される処理を登録する仕組み。
// Vue版のmain.tsが「createApp → プラグイン登録 → mount」という一連の処理を
// 手続き的に1ファイルに書いていたのに対し、Nuxtでは「アプリ初期化時にやりたいこと」を
// plugins/ 配下にファイルとして置くだけで、Nuxt側が適切なタイミングで自動実行してくれる。
//
// 【なぜ .client. を付けず、サーバー・クライアント両方で実行するのか】
// 最初はファイル名を `restore-session.client.ts` にして「クライアントのみ実行」に
// していたが、それだと以下の問題が起きることが実機テストで判明した:
//   1. ユーザーが /admin-demo のようなmiddleware（middleware/admin-only.ts）付きページに
//      直接アクセス（ブラウザのリロードやURL直接入力）すると、Nuxtはまずサーバー側で
//      SSRを行い、その過程でmiddlewareも一度サーバー側で評価される。
//   2. このときrestoreSessionがまだ実行されていない（クライアント専用プラグインは
//      サーバーでは動かない）ため、authStore.isLoggedInは常にfalseのままで、
//      実際にはログイン済みのはずなのにmiddlewareに弾かれて/loginへ飛ばされてしまう。
// この不具合を避けるため、ファイル名から `.client` を外し、サーバー・クライアント両方の
// 実行タイミングでrestoreSessionを呼ぶようにした。サーバー側で呼ばれた場合は、
// utils/graphqlClient.tsがuseRequestHeaders(['cookie'])でブラウザのCookieを
// backendへの問い合わせに転送するため、SSRの時点で正しいログイン状態を判定できる。
export default defineNuxtPlugin(async () => {
  const authStore = useAuthStore()
  try {
    await authStore.restoreSession()
  } catch {
    // 未ログイン・通信エラーのいずれでも、単に非ログイン状態のまま表示を続ければよい。
  }
})
