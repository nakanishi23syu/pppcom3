<!--
  ======================================================
  ReadingStatusBadge.vue — 読影ステータスの色付きバッジ（ドロップダウン）
  ======================================================
  Vue版 frontend/src/features/study/components/ReadingStatusBadge.vue の移植。
  見た目は色付きの丸角バッジだが、実体は透明化した<select>を重ねているだけ。
-->

<template>
  <div class="status-badge" :class="`status-${status}`" @click.stop>
    <select
      class="status-select"
      :value="status"
      @change="$emit('update:status', ($event.target as HTMLSelectElement).value as ReadingStatus)"
    >
      <option v-for="option in READING_STATUS_OPTIONS" :key="option" :value="option">
        {{ READING_STATUS_LABELS[option] }}
      </option>
    </select>
    <span class="status-label">{{ READING_STATUS_LABELS[status] }}</span>
  </div>
</template>

<script setup lang="ts">
// READING_STATUS_LABELS / READING_STATUS_OPTIONS はcomposables/useReadingStatus.tsの
// value exportなのでauto-importで解決されるが、ReadingStatus型だけは明示的にimportする。
import type { ReadingStatus } from '~/composables/useReadingStatus'

defineProps<{
  status: ReadingStatus
}>()

defineEmits<{
  'update:status': [status: ReadingStatus]
}>()
</script>

<style scoped>
.status-badge {
  position: relative;
  display: inline-flex;
  align-items: center;
  border-radius: 4px;
  padding: 2px 10px;
  font-size: 0.75rem;
  font-weight: 600;
  cursor: pointer;
  white-space: nowrap;
}

.status-select {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  opacity: 0;
  cursor: pointer;
}

.status-not-entered {
  background: var(--color-border);
  color: var(--color-text-muted);
}

.status-in-progress {
  background: var(--color-success-bg);
  color: var(--color-success);
}

.status-draft-saved {
  background: var(--color-warning);
  color: var(--color-text-heading);
}

.status-finalized {
  background: var(--color-purple-bg);
  color: var(--color-purple);
}
</style>
