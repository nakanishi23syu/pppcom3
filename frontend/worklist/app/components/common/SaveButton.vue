<!--
  ======================================================
  SaveButton.vue — 保存ボタン（BaseButtonのラッパー）
  ======================================================
  「保存ボタンが欲しい場所ではとりあえずこれを置けばよい」variant="primary" + 既定文言「保存」。
-->

<template>
  <BaseButton variant="primary" :disabled="disabled" :full-width="fullWidth" :type="type">
    <slot>保存</slot>
  </BaseButton>
</template>

<script setup lang="ts">
withDefaults(
  defineProps<{
    disabled?: boolean
    fullWidth?: boolean
    type?: 'button' | 'submit'
  }>(),
  {
    disabled: false,
    fullWidth: false,
    type: 'button',
  }
)
</script>
