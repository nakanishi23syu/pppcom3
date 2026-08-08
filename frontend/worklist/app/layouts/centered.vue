<!--
  ======================================================
  layouts/centered.vue — 画面中央にカード状のコンテンツを置くレイアウト
  ======================================================
  ログインページのように「画面中央に小さなカードを1枚置くだけ」のページ専用レイアウト。
  pages/login.vue 側で
    definePageMeta({ layout: 'centered' })
  と書くだけで、このレイアウトに切り替わる（ファイル名 centered.vue の "centered" が
  そのままレイアウト名になる）。

  Vue版にはこの仕組みが無く、LoginView.vue自身が
  `.page { min-height: 100vh; display:flex; align-items:center; justify-content:center; }`
  というラッパーCSSを毎回自分で持っていた。Nuxtでは「中央寄せの外枠」をレイアウトとして
  切り出しておくと、将来「登録ページ」のような似た構成のページを増やしたときに
  definePageMeta({ layout: 'centered' }) の1行を足すだけで再利用できる。
-->

<template>
  <div class="centered-page">
    <slot />
  </div>
</template>

<style scoped>
.centered-page {
  min-height: 100vh;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 1.5rem;
}
</style>
