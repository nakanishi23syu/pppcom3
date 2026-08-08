<!--
  ======================================================
  NotificationModal.vue — 通知ポップアップ（モーダル）
  ======================================================
  「保存しました」「エラーが発生しました」等、ユーザーに一方的に伝えるだけの通知用モーダル。
  ボタンは「閉じる」の1つだけ。Yes/No選択が必要な場合は ConfirmDialog.vue を使う。
-->

<template>
  <BaseModal
    :model-value="modelValue"
    :title="title"
    @update:model-value="$emit('update:modelValue', $event)"
    @close="$emit('close')"
  >
    <slot />

    <template #footer>
      <BaseButton variant="primary" @click="$emit('update:modelValue', false)">
        {{ closeText }}
      </BaseButton>
    </template>
  </BaseModal>
</template>

<script setup lang="ts">
withDefaults(
  defineProps<{
    modelValue: boolean
    title?: string
    closeText?: string
  }>(),
  {
    title: '',
    closeText: '閉じる',
  }
)

defineEmits<{
  'update:modelValue': [value: boolean]
  close: []
}>()
</script>
