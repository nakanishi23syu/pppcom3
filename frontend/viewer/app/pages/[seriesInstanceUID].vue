<!--
  ======================================================
  pages/[seriesInstanceUID].vue — 画像ビューアページ（トップレベルの動的ルート）
  ======================================================
  移植元(dicom-tool-2/nuxt)では pages/viewer/[seriesInstanceUID].vue として
  ワークリストと同居するアプリの中の1ページ（"/viewer/:seriesInstanceUID"）だったが、
  このプロジェクトではアプリ自体が「画像を表示するだけの専用サービス」になったため、
  "/viewer" という接頭辞を省いてアプリのルート直下に置いている
  （URL例: http://localhost:3200/1.2.840.113619.2.55...）。
  Worklist側は `${viewerEndpoint}/${series.seriesInstanceUID}` という形でこのURLへ
  新しいタブを開く（frontend/worklist/app/pages/index.vue の openSeries() 参照）。

  【直接URLアクセスされても動くようにする（C章対応）】
  Timelineアプリ（別プロセス、Blazor/5230）からもwindow.open等でこのURLを直接開く想定のため、
  「Worklist経由での画面遷移」を前提にできない。store（Piniaのdicomストア）はこのアプリの
  中だけで完結しており、Worklistの状態を引き継ぐことはそもそもできない（別プロセスの別Piniaの
  インスタンスのため）。そのためこのページは常に「自分でGraphQLに問い合わせて必要なデータを
  揃える」実装にしている。認証はhttpOnly Cookie経由（utils/graphqlClient.ts参照）で行われるため、
  Backend APIから見れば「Worklistでログインしたブラウザ」からのリクエストと区別が付かず、
  Viewerだけを直接開いても同じログイン状態のままGraphQL呼び出しが認証される。
-->

<template>
  <div class="page">
    <header class="page-header">
      <div v-if="series">
        <h1>{{ series.seriesDescription || `Series ${series.seriesNumber}` }}</h1>
        <p class="subtitle">{{ series.modality || '—' }}</p>
      </div>
    </header>

    <main class="page-main">
      <div v-if="store.loading" class="state-msg">
        <span class="spinner" />
        読み込み中...
      </div>
      <div v-else-if="store.error" class="state-msg error">{{ store.error }}</div>
      <div v-else-if="!series" class="state-msg error">
        指定されたシリーズが見つかりませんでした。
      </div>
      <SeriesViewer v-else :series="series" />
    </main>
  </div>
</template>

<script setup lang="ts">
const route = useRoute()
const store = useDicomStore()

// Viewerは常に独立して起動される（Worklistのstoreとは別プロセスの別インスタンス）ため、
// このページが開かれた時点でstudiesが空である前提で、必ず自前でロードし直す。
onMounted(() => {
  if (store.studies.length === 0) store.fetchStudies()
})

const series = computed(() => {
  const uid = route.params.seriesInstanceUID as string
  for (const study of store.studies) {
    const found = study.series.find((s) => s.seriesInstanceUID === uid)
    if (found) return found
  }
  return null
})
</script>

<style scoped>
.page {
  height: 100vh;
  display: flex;
  flex-direction: column;
}

.page-header {
  padding: 0.85rem 1.5rem;
  background: var(--color-surface);
  border-bottom: 1px solid var(--color-border);
  flex-shrink: 0;
}

.page-header h1 {
  margin: 0;
  font-size: 1rem;
  color: var(--color-text-heading);
}

.subtitle {
  margin: 0.15rem 0 0;
  font-size: 0.78rem;
  color: var(--color-text-muted);
}

.page-main {
  flex: 1;
  overflow: hidden;
}

.state-msg {
  height: 100%;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 0.5rem;
  color: var(--color-text-muted);
}

.state-msg.error {
  color: var(--color-danger);
}

.spinner {
  display: inline-block;
  width: 20px;
  height: 20px;
  border: 2px solid var(--color-border-strong);
  border-top-color: var(--color-accent);
  border-radius: 50%;
  animation: spin 0.7s linear infinite;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}
</style>
