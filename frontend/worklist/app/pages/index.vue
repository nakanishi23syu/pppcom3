<!--
  ======================================================
  pages/index.vue — 検査一覧ページ（ワークリスト）
  ======================================================
  dicom-tool-2/nuxt からの移植。実際のPACS製品のワークリスト画面の構成に近づけている:
    - 左サイドバー（検索プリセット / 分類フォルダ）
    - 検査を選択すると「同一患者IDの全検査」「シリーズリスト」が下に常設表示される

  【サービス分離に伴う変更点】
  - 元のdicom-tool-2/nuxtでは同居していたNuxt学習チュートリアル(/tutorial)は、
    サービス分離の対象外（Worklist本来の機能ではない）としてこのプロジェクトには含めていない。
  - シリーズをダブルクリックした際の遷移先(openSeries)は、元は同一アプリ内の
    pages/viewer/[seriesInstanceUID].vue へのNuxt内部ルーティング(router.push)だったが、
    Viewerは別プロセス・別ポート(3200)の独立したNuxtアプリに分離されたため、
    Nuxtのクライアントサイドルーティングでは遷移できない。ブラウザの通常の画面遷移
    （新しいタブでのwindow.open）に置き換えている。
-->

<template>
  <div class="page">
    <header class="page-header">
      <div class="logo">
        <span class="logo-icon">⬡</span>
        <span class="logo-text">DICOM Tool - Worklist</span>
      </div>
      <div class="header-actions">
        <span v-if="store.studies.length > 0" class="study-count">
          {{ store.studies.length }} 件
        </span>
        <label class="auto-refresh">
          <input v-model="autoRefresh" type="checkbox" />
          自動更新
        </label>
        <NuxtLink to="/upload" class="tutorial-link">⬆ アップロード</NuxtLink>
        <button class="refresh-btn" :disabled="store.loading" @click="store.fetchStudies()">
          <span :class="{ spinning: store.loading }">↻</span>
          更新
        </button>
        <div v-if="authStore.isLoggedIn" class="auth-status">
          <span class="auth-name">
            👤 {{ authStore.displayName }}
            <span v-if="authStore.isAdmin" class="admin-badge">管理者</span>
          </span>
          <button class="auth-link" @click="authStore.logout()">ログアウト</button>
        </div>
        <NuxtLink v-else to="/login" class="tutorial-link">🔑 ログイン</NuxtLink>
      </div>
    </header>

    <div class="page-body">
      <WorklistSidebar @select-preset="handleSelectPreset" />

      <main class="page-main">
        <div class="toolbar">
          <h1 class="section-title">検査一覧</h1>
        </div>

        <StudyTable
          :studies="store.studies"
          :loading="store.loading"
          :error="store.error"
          :selected-u-i-d="selectedStudy?.studyInstanceUID ?? null"
          @select-study="selectedStudy = $event"
          @data-changed="handleDataChanged"
        />

        <PatientHistoryPanel
          v-if="selectedStudy"
          :studies="store.studies"
          :selected-study="selectedStudy"
          @select-study="selectedStudy = $event"
        />
        <SeriesListPanel
          v-if="selectedStudy"
          :key="selectedStudy.studyInstanceUID"
          :study="selectedStudy"
          @open-images="openSeries"
          @data-changed="handleDataChanged"
        />
      </main>
    </div>
  </div>
</template>

<script setup lang="ts">
import type { DicomStudy, DicomSeries } from '~/types/dicom'

const config = useRuntimeConfig()

const store = useDicomStore()
const authStore = useAuthStore()

const selectedStudy = ref<DicomStudy | null>(null)

// シリーズがダブルクリックされたら、別プロセスのViewerアプリ(3200)を新しいタブで開く。
// Viewer側は pages/[seriesInstanceUID].vue（Viewerプロジェクト内）がこのURLパスに対応する。
// GraphQL呼び出しの認証はCookie経由（httpOnly、Backend APIのホストに紐づく）で行われるため、
// このアプリでログインしていれば別タブのViewerでも同じログイン状態のままデータを取得できる。
function openSeries(series: DicomSeries) {
  window.open(`${config.public.viewerEndpoint}/${series.seriesInstanceUID}`, '_blank')
}

async function handleDataChanged() {
  const selectedUid = selectedStudy.value?.studyInstanceUID ?? null
  await store.fetchStudies()
  selectedStudy.value = selectedUid
    ? (store.studies.find((s) => s.studyInstanceUID === selectedUid) ?? null)
    : null
}

function handleSelectPreset(name: string) {
  if (name === '全体') {
    selectedStudy.value = null
  }
}

const autoRefresh = ref(false)
let autoRefreshTimer: ReturnType<typeof setInterval> | null = null

watch(autoRefresh, (enabled) => {
  if (autoRefreshTimer) {
    clearInterval(autoRefreshTimer)
    autoRefreshTimer = null
  }
  if (enabled) {
    autoRefreshTimer = setInterval(() => store.fetchStudies(), 30_000)
  }
})

onUnmounted(() => {
  if (autoRefreshTimer) clearInterval(autoRefreshTimer)
})

// ページが表示されたら自動的に DICOM データを読み込む。
onMounted(() => store.fetchStudies())
</script>

<style scoped>
.page {
  height: 100vh;
  display: flex;
  flex-direction: column;
  background: var(--color-bg);
}

.page-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 1.5rem;
  height: 52px;
  background: var(--color-surface);
  border-bottom: 1px solid var(--color-border);
  flex-shrink: 0;
}

.logo {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.logo-icon {
  color: var(--color-accent);
  font-size: 1.3rem;
}

.logo-text {
  font-size: 1rem;
  font-weight: 600;
  color: var(--color-text-heading);
  letter-spacing: 0.03em;
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.auto-refresh {
  display: flex;
  align-items: center;
  gap: 0.3rem;
  font-size: 0.8rem;
  color: var(--color-text-muted);
  cursor: pointer;
  margin-right: 0.25rem;
}

.tutorial-link {
  font-size: 0.85rem;
  color: var(--color-accent);
  text-decoration: none;
  padding: 0.35rem 0.6rem;
}

.tutorial-link:hover {
  text-decoration: underline;
}

.auth-status {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-left: 0.25rem;
  padding-left: 0.75rem;
  border-left: 1px solid var(--color-border);
}

.auth-name {
  font-size: 0.8rem;
  color: var(--color-text-muted);
  display: flex;
  align-items: center;
  gap: 0.4rem;
}

.admin-badge {
  font-size: 0.68rem;
  background: var(--color-accent-bg);
  color: var(--color-accent);
  border-radius: 10px;
  padding: 1px 7px;
}

.auth-link {
  background: none;
  border: none;
  color: var(--color-accent);
  font-size: 0.8rem;
  cursor: pointer;
  padding: 0.2rem;
}

.auth-link:hover {
  text-decoration: underline;
}

.refresh-btn {
  background: var(--color-accent-bg);
  color: var(--color-accent);
  border: 1px solid var(--color-border-strong);
  border-radius: 5px;
  padding: 0.35rem 0.85rem;
  font-size: 0.85rem;
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 0.4rem;
  transition: background 0.15s;
}

.refresh-btn:hover:not(:disabled) {
  background: var(--color-accent-bg-hover);
}

.refresh-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.spinning {
  display: inline-block;
  animation: spin 0.7s linear infinite;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}

.page-body {
  flex: 1;
  display: flex;
  overflow: hidden;
}

.page-main {
  flex: 1;
  overflow: auto;
  display: flex;
  flex-direction: column;
}

.toolbar {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 1rem 1.5rem 0.75rem;
  border-bottom: 1px solid var(--color-border);
}

.section-title {
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--color-text-muted);
  text-transform: uppercase;
  letter-spacing: 0.08em;
}

.study-count {
  font-size: 0.78rem;
  background: var(--color-accent-bg);
  color: var(--color-accent);
  border-radius: 10px;
  padding: 1px 8px;
}
</style>
