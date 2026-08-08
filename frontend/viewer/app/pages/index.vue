<!--
  ======================================================
  pages/index.vue — Viewerアプリのルート（案内表示のみ）
  ======================================================
  Viewerは「seriesInstanceUIDを指定して初めて意味を持つ」単機能サービスのため、
  "/"（何も指定しないアクセス）に対応する専用の画面は元のdicom-tool-2/nuxtには存在しない。
  何も表示せず404にすることもできるが、直接 http://localhost:3200/ を開いてしまった場合に
  何が起きているか分かるよう、簡単な案内だけを表示する軽量なページを用意している。
-->

<template>
  <div class="page">
    <p class="message">
      表示するシリーズが指定されていません。<br />
      Worklist（検査一覧）からシリーズをダブルクリックして開いてください。
    </p>
  </div>
</template>

<style scoped>
.page {
  height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 1.5rem;
}

.message {
  text-align: center;
  font-size: 0.9rem;
  line-height: 1.8;
  color: var(--color-text-muted);
}
</style>
