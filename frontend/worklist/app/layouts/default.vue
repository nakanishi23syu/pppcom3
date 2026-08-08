<!--
  ======================================================
  layouts/default.vue — 既定レイアウト
  ======================================================
  【Nuxtのレイアウトとは】
  layouts/ 配下に置いたコンポーネントは「複数ページで共通の外枠」として使える。
  何も指定しなければ、すべてのページに自動的に layouts/default.vue が適用される
  （Vue版にはこの概念自体が無く、各View（views/*.vue）が毎回同じヘッダー構造を
  自分のテンプレート内に書いていた）。

  このプロジェクトの各ページ（検査一覧・タイムライン・アップロード等）は、Vue版の
  views/*.vue を踏襲してそれぞれ自前のヘッダー（戻るリンク・タイトル）を持っているため、
  既定レイアウトは "何もしない透過ラッパー"（<slot/>をそのまま出すだけ）にとどめている。
  ログインページだけは layouts/centered.vue という別レイアウトを使う例を用意した
  （pages/login.vue の definePageMeta({ layout: 'centered' }) を参照）。
  詳しい解説は features/tutorial/components/FileRoutingLayoutUnit.vue を参照。
-->

<template>
  <slot />
</template>
