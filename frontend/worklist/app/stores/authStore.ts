// ======================================================
// stores/authStore.ts — ログイン状態のグローバル管理（Pinia Store）
// ======================================================
// Vue版 frontend/src/stores/authStore.ts の移植。ロジックは完全に同一。
// @pinia/nuxt モジュールを nuxt.config.ts の modules に登録しているため、
// defineStore は import なしでそのまま使える（Nuxtのauto-import。PiniaStoreUnit.vue参照）。
//
// 【セキュリティ対応】JWTはbackendがhttpOnly Cookieとして発行する方式のため、
// フロントエンドはトークンの値を一切保持しない。ページリロード後のログイン状態復元は
// restoreSession() が backend の me クエリに問い合わせる方式にしている
// （Cookieが有効ならdisplayName/isAdminを返してくれる）。
//
// utils/authService.ts の login/logout/me は、このファイル内でも同名のaction
// （下のlogin/logout関数）を定義したいため、別名を付けてimportしておく
// （auto-importされるグローバル関数と同名のローカル変数を作ると、このファイル内では
// ローカル定義が優先されるが紛らわしいため、明示的にエイリアスして衝突を避けている）。
import { login as loginRequest, logout as logoutRequest, me as meRequest } from '~/utils/authService'

export const useAuthStore = defineStore('auth', () => {
  // ── State ──────────────────────────────────────────────
  const displayName = ref<string>('')
  const isAdmin = ref<boolean>(false)
  const isLoggedIn = ref(false)
  const loading = ref(false)
  const error = ref<string | null>(null)

  // ── Actions ────────────────────────────────────────────
  async function login(username: string, password: string) {
    loading.value = true
    error.value = null
    try {
      const payload = await loginRequest(username, password)
      displayName.value = payload.displayName
      isAdmin.value = payload.isAdmin
      isLoggedIn.value = true
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'ログインに失敗しました'
      throw e
    } finally {
      loading.value = false
    }
  }

  async function logout() {
    try {
      await logoutRequest()
    } finally {
      displayName.value = ''
      isAdmin.value = false
      isLoggedIn.value = false
    }
  }

  // アプリ起動時（plugins/restore-session.client.ts）に1回呼び出し、
  // Cookieがまだ有効ならログイン状態を復元する。
  async function restoreSession() {
    const result = await meRequest()
    if (result) {
      displayName.value = result.displayName
      isAdmin.value = result.isAdmin
      isLoggedIn.value = true
    }
  }

  return { displayName, isAdmin, isLoggedIn, loading, error, login, logout, restoreSession }
})
