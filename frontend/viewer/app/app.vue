<!--
  ======================================================
  app.vue — アプリのルートコンポーネント
  ======================================================
  移植元(dicom-tool-2/nuxt)のapp.vueは<ToastContainer/>も併設していたが、Viewerに
  切り出したcomponents/common・ページ群はどこもトースト通知(useToast)を使わないため、
  ここでは<NuxtLayout><NuxtPage/></NuxtLayout>のみのシンプルな構成にしている。
-->

<template>
  <NuxtLayout>
    <NuxtPage />
  </NuxtLayout>
</template>
