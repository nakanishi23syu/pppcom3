<!--
  ======================================================
  BaseModal.vue — 汎用モーダル（ポップアップ）
  ======================================================
  Vue版 frontend/src/components/common/BaseModal.vue の移植（挙動は完全に同一）。
  NotificationModal.vue（通知ポップアップ）・ConfirmDialog.vue（yes/noポップアップ）は
  どちらもこのBaseModalの上に組み立てている。

  【使い方】
    <BaseModal v-model="isOpen" title="タイトル">
      本文はここにお好きな内容を書ける（デフォルトスロット）
      <template #footer>
        <CancelButton @click="isOpen = false" />
        <SaveButton @click="onSave" />
      </template>
    </BaseModal>
-->

<template>
  <!--
    【NuxtのSSRとTeleportの組み合わせについて】
    <Teleport to="body"> は「このコンポーネントの描画結果を、DOMツリー上は別の場所
    （ここでは<body>直下）に差し込む」という機能。モーダルやトースト通知のように
    「見た目の親子関係と実際のDOM上の位置を分離したい」場合に使う（Vue版でも同じ理由で使用）。

    ただしNuxtのSSR環境では、サーバー側でレンダリングした<body>の中身（Teleport先を
    含む）とクライアント側の初期状態がわずかに食い違い、hydration（サーバーが生成した
    HTMLに対してクライアント側のVueが「魂を吹き込む」処理）でミスマッチ警告が出ることが
    実機テストで判明した。モーダル・トーストはそもそも「初期表示時点では非表示」
    「ユーザー操作でのみ開く」ものでSSRする意味が薄いため、<ClientOnly>で包んで
    「サーバーでは一切描画せず、クライアント側でマウントされてから初めて描画する」
    ようにし、hydrationミスマッチを避けている。
  -->
  <ClientOnly>
    <Teleport to="body">
      <div v-if="modelValue" class="overlay" @click.self="handleClose">
        <div class="modal" role="dialog" aria-modal="true">
          <div class="modal-header">
            <div class="modal-title">
              <slot name="header">
                <h2 v-if="title">{{ title }}</h2>
              </slot>
            </div>
            <button class="close-btn" aria-label="閉じる" @click="handleClose">✕</button>
          </div>

          <div class="modal-body">
            <slot />
          </div>

          <div v-if="$slots.footer" class="modal-footer">
            <slot name="footer" />
          </div>
        </div>
      </div>
    </Teleport>
  </ClientOnly>
</template>

<script setup lang="ts">
const props = defineProps<{
  modelValue: boolean
  title?: string
}>()

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  close: []
}>()

function handleClose() {
  emit('update:modelValue', false)
  emit('close')
}

// ======================================================
// Escキーで閉じる
// ======================================================
// window.addEventListener はブラウザにしか存在しないため、Nuxtの
// onMounted/onUnmounted と組み合わせて「クライアントで表示されている間だけ」登録する。
// （このコンポーネント自体はSSR時にも1回描画されるが、v-ifがfalseなので中身は出力されない）
function handleKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape') {
    handleClose()
  }
}

watch(
  () => props.modelValue,
  (isOpen) => {
    if (!import.meta.client) return
    if (isOpen) {
      window.addEventListener('keydown', handleKeydown)
    } else {
      window.removeEventListener('keydown', handleKeydown)
    }
  },
  { immediate: true }
)

onUnmounted(() => {
  if (import.meta.client) window.removeEventListener('keydown', handleKeydown)
})
</script>

<style scoped>
.overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.65);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 200;
}

.modal {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 8px;
  width: 480px;
  max-width: 95vw;
  max-height: 85vh;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.modal-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  padding: 1.1rem 1.25rem;
  border-bottom: 1px solid var(--color-border);
}

.modal-title h2 {
  margin: 0;
  font-size: 1.05rem;
  color: var(--color-text-heading);
}

.close-btn {
  background: none;
  border: none;
  color: var(--color-text-muted);
  font-size: 1.1rem;
  cursor: pointer;
  padding: 0 0.25rem;
  line-height: 1;
  transition: color 0.15s;
}

.close-btn:hover {
  color: var(--color-text-heading);
}

.modal-body {
  overflow-y: auto;
  padding: 1.25rem;
  font-size: 0.88rem;
  color: var(--color-text);
  line-height: 1.7;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 0.6rem;
  padding: 1rem 1.25rem;
  border-top: 1px solid var(--color-border);
}
</style>
