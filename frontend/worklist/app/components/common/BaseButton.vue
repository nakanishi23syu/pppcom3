<!--
  ======================================================
  BaseButton.vue — 汎用ボタン
  ======================================================
  Vue版 frontend/src/components/common/BaseButton.vue の移植。
  アプリ内のあらゆるボタン（保存・キャンセル・OK等）の土台になるコンポーネント。
  SaveButton.vue / CancelButton.vue はこれを「見た目の初期値だけ変えて」ラップしたもの。

  【Nuxtのコンポーネントauto-importについて】
  Vue版では他コンポーネントから使う際に
    import BaseButton from '@/components/common/BaseButton.vue'
  という1行が必要だったが、Nuxtでは nuxt.config.ts の components 設定により
  components/common/ 配下のファイルは自動でグローバル登録される。
  そのため、このファイル（や他のどのコンポーネントからも）<BaseButton> とタグを
  書くだけで使え、import文は一切不要になる（AutoImportUnit.vue で詳しく解説する）。
-->

<template>
  <button
    :type="type"
    class="base-button"
    :class="[`variant-${variant}`, { 'is-full-width': fullWidth }]"
    :disabled="disabled"
  >
    <slot />
  </button>
</template>

<script setup lang="ts">
withDefaults(
  defineProps<{
    variant?: 'primary' | 'secondary' | 'danger'
    disabled?: boolean
    fullWidth?: boolean
    type?: 'button' | 'submit'
  }>(),
  {
    variant: 'primary',
    disabled: false,
    fullWidth: false,
    type: 'button',
  }
)
</script>

<style scoped>
.base-button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 0.4rem;
  border-radius: 6px;
  padding: 0.5rem 1.1rem;
  font-size: 0.85rem;
  font-weight: 600;
  cursor: pointer;
  border: 1px solid transparent;
  transition:
    background 0.15s,
    border-color 0.15s,
    opacity 0.15s;
}

.base-button.is-full-width {
  width: 100%;
}

.base-button:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.variant-primary {
  background: var(--color-accent-bg);
  border-color: var(--color-border-strong);
  color: var(--color-accent);
}

.variant-primary:not(:disabled):hover {
  background: var(--color-accent-bg-hover);
}

.variant-secondary {
  background: transparent;
  border-color: var(--color-border);
  color: var(--color-text-muted);
}

.variant-secondary:not(:disabled):hover {
  color: var(--color-text);
  border-color: var(--color-border-strong);
}

.variant-danger {
  background: var(--color-danger-bg);
  border-color: var(--color-danger-border);
  color: var(--color-danger);
}

.variant-danger:not(:disabled):hover {
  opacity: 0.85;
}
</style>
