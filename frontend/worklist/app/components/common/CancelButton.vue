<!--
  ======================================================
  CancelButton.vue — キャンセルボタン（BaseButtonのラッパー）
  ======================================================
  Vue版と同じく variant="secondary" + 既定の文言「キャンセル」をあらかじめ設定している。
  <BaseButton> はNuxtのauto-importで解決されるため、ここでもimport文は不要。
-->

<template>
  <BaseButton variant="secondary" :disabled="disabled" :full-width="fullWidth">
    <slot>キャンセル</slot>
  </BaseButton>
</template>

<script setup lang="ts">
withDefaults(
  defineProps<{
    disabled?: boolean
    fullWidth?: boolean
  }>(),
  {
    disabled: false,
    fullWidth: false,
  }
)
</script>
