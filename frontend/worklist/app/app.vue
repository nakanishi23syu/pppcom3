<!--
  ======================================================
  app.vue — アプリのルートコンポーネント
  ======================================================
  移植元(dicom-tool-2/nuxt)のapp.vueと同一。<NuxtLayout><NuxtPage/></NuxtLayout>で
  pages/配下のファイルを描画し、<ToastContainer/>で保存・削除操作の結果を通知する。
-->

<template>
  <NuxtLayout>
    <NuxtPage />
  </NuxtLayout>
  <ToastContainer />
</template>
