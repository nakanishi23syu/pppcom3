// ======================================================
// stores/dicomStore.ts — DICOM グローバル状態管理（Pinia Store）
// ======================================================
// Vue版 frontend/src/stores/dicomStore.ts の移植（ロジックは完全に同一）。
//
// 【Pinia（ピニャ）とは？】
// Vue 3 公式の状態管理ライブラリ。Vuex の後継。
// 「Store（ストア）」はアプリ全体で共有される状態（データ）の置き場所。
// コンポーネントの ref は「そのコンポーネントが生きている間だけ」存在するが、
// Store のデータは「アプリが起動している間ずっと」保持される。ページ遷移しても
// studies データが消えない、のが Store の特徴。
//
// 【NuxtでのPiniaの扱い】
// @pinia/nuxt モジュール（nuxt.config.tsのmodulesに登録済み）が、Vue版main.tsの
//   app.use(createPinia())
// に相当するセットアップを自動で行ってくれる。defineStore自体もauto-importされる。
//
// fetchStudies は Store 内の action 名（fetchStudies）と、utils/backendApiService.ts が
// exportする同名関数が衝突するため、こちらだけ別名でimportする（authStore.tsのlogin/logoutと同じ理由）。
import { fetchStudies as fetchStudiesFromBackend } from '~/utils/backendApiService'
import type { DicomStudy } from '~/types/dicom'

export const useDicomStore = defineStore('dicom', () => {
  // ── State（状態）────────────────────────────────────────
  const studies = ref<DicomStudy[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)

  // ── Actions（状態を変更する関数）────────────────────────
  async function fetchStudies() {
    loading.value = true
    error.value = null
    try {
      const backendStudies = await fetchStudiesFromBackend()
      studies.value = backendStudies.map(mapBackendStudy)
    } catch (e) {
      error.value = e instanceof Error ? e.message : '検査データの読み込みに失敗しました'
    } finally {
      loading.value = false
    }
  }

  return { studies, loading, error, fetchStudies }
})
