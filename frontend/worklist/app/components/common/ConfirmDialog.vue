<!--
  ======================================================
  ConfirmDialog.vue — yes / no ポップアップ
  ======================================================
  「本当に削除しますか？」のような、はい/いいえの二択を確認するモーダル。
-->

<template>
  <BaseModal
    :model-value="modelValue"
    :title="title"
    @update:model-value="$emit('update:modelValue', $event)"
  >
    <slot />

    <template #footer>
      <CancelButton @click="handleCancel">{{ cancelText }}</CancelButton>
      <SaveButton @click="handleConfirm">{{ confirmText }}</SaveButton>
    </template>
  </BaseModal>
</template>

<script setup lang="ts">
const props = withDefaults(
  defineProps<{
    modelValue: boolean
    title?: string
    confirmText?: string
    cancelText?: string
    closeOnConfirm?: boolean
  }>(),
  {
    title: '',
    confirmText: 'はい',
    cancelText: 'いいえ',
    closeOnConfirm: true,
  }
)

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  confirm: []
  cancel: []
}>()

function handleConfirm() {
  emit('confirm')
  if (props.closeOnConfirm) {
    emit('update:modelValue', false)
  }
}

function handleCancel() {
  emit('cancel')
  emit('update:modelValue', false)
}
</script>
