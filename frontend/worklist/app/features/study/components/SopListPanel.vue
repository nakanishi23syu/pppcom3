<!--
  ======================================================
  SopListPanel.vue — 「SOP（画像）一覧」パネル
  ======================================================
  Vue版 frontend/src/features/study/components/SopListPanel.vue の移植。
  StudyTable.vue（検査一覧）・SeriesListPanel.vue（シリーズ一覧）と同じ
  「ドラッグで並べ替え → 保存でDBのOrderカラムへ反映 → 適用でDB順に戻す」の
  パターンを、共通のcomposable（useEditableList・useDragSort）で実現している。
-->

<template>
  <section class="sop-panel">
    <div class="panel-header">
      <span class="panel-title">▼ SOP（画像）一覧</span>
      <span class="panel-count">件数：{{ series.instances.length }}</span>
      <span class="reorder-actions">
        <button
          class="reorder-btn"
          :disabled="!editable.dirty.value || editable.saving.value"
          title="変更した内容をDBに保存します"
          @click="handleSave"
        >
          💾 保存
        </button>
        <button
          class="reorder-btn"
          :disabled="!editable.dirty.value || editable.saving.value"
          @click="editable.apply()"
        >
          ↺ 元に戻す
        </button>
        <span v-if="editable.dirty.value" class="dirty-hint">未保存</span>
        <span v-if="!authStore.isAdmin" class="dirty-hint">※並べ替えは管理者のみ</span>
        <span v-if="editable.saveError.value" class="reorder-error">
          {{ editable.saveError.value }}
        </span>
      </span>
      <span v-if="checkable.checkedIds.value.size > 0" class="checked-actions">
        <button class="revert-btn" :disabled="reverting" @click="handleRevertChecked">
          🔄 選択した{{ checkable.checkedIds.value.size }}件をDICOMタグの値に戻す
        </button>
        <button class="delete-selected-btn" @click="showDeleteConfirm = true">
          🗑 選択した{{ checkable.checkedIds.value.size }}件を削除
        </button>
      </span>
    </div>

    <table class="sop-table">
      <thead>
        <tr>
          <th class="check-col" />
          <th class="drag-col" />
          <th>画像番号</th>
          <th>SOP Instance UID</th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="(instance, index) in editable.workingItems.value"
          :key="instance.sopInstanceUID"
          class="sop-row"
          :class="{ dragging: draggingIndex === index }"
          v-bind="dropTargetProps(index)"
        >
          <td class="check-col" @click.stop>
            <input
              type="checkbox"
              :checked="checkable.isChecked(instance)"
              @change="checkable.toggle(instance)"
            />
          </td>
          <td class="drag-col">
            <span class="drag-handle" title="ドラッグで並べ替え" v-bind="dragHandleProps(index)">
              ⠿
            </span>
          </td>
          <td>
            <input
              v-if="checkable.isChecked(instance)"
              v-model="instance.instanceNumber"
              class="cell-input"
              @click.stop
            />
            <template v-else>{{ instance.instanceNumber || '—' }}</template>
          </td>
          <td class="uid-cell">{{ instance.sopInstanceUID }}</td>
        </tr>
      </tbody>
    </table>

    <ConfirmDialog
      v-model="showDeleteConfirm"
      title="画像の削除"
      confirm-text="削除する"
      @confirm="handleDeleteChecked"
    >
      選択した{{ checkable.checkedIds.value.size }}件の画像を削除します。
      DBのレコードと実ファイルも削除され、元に戻せません。よろしいですか？
    </ConfirmDialog>
  </section>
</template>

<script setup lang="ts">
import type { DicomSeries, DicomInstance } from '~/types/dicom'
import type { SopChangeInput } from '~/utils/backendApiService'

const props = defineProps<{
  series: DicomSeries
}>()

const emit = defineEmits<{
  'data-changed': []
}>()

const authStore = useAuthStore()
const toast = useToast()

// 呼び出し側で :key="series.seriesInstanceUID" を付けてもらう想定
// （シリーズが切り替わるとこのコンポーネント自体が再マウントされ、
// 前のシリーズの未保存の編集状態を引きずらない）。
const instances = computed(() => props.series.instances)
const editable = useEditableList(instances, {
  getId: (i: DicomInstance) => i.sopInstanceUID,
  getOrder: (i: DicomInstance) => i.order,
  getFields: (i: DicomInstance) => ({ instanceNumber: i.instanceNumber }),
})

const { draggingIndex, dragHandleProps, dropTargetProps } = useDragSort(editable.workingItems)

async function handleSave() {
  try {
    const count = await editable.save(
      (instance, patch): SopChangeInput => ({
        sopInstanceUid: instance.sopInstanceUID,
        order: patch.order,
        instanceNumber: patch.fields?.instanceNumber,
      }),
      saveSopChanges
    )
    if (count > 0) toast.success(`${count}件の変更を保存しました`)
  } catch (e) {
    toast.error(e instanceof Error ? e.message : '保存に失敗しました')
  }
}

const checkable = useCheckableRows<DicomInstance>((i) => i.sopInstanceUID)
const showDeleteConfirm = ref(false)
const reverting = ref(false)

async function handleRevertChecked() {
  const ids = [...checkable.checkedIds.value]
  reverting.value = true
  try {
    for (const id of ids) {
      const reverted = await revertSopFields(id)
      const instance = instances.value.find((i) => i.sopInstanceUID === id)
      if (!instance) continue
      instance.instanceNumber = reverted.instanceNumber
    }
  } catch (e) {
    toast.error(e instanceof Error ? e.message : 'DICOMタグへの復元に失敗しました')
  } finally {
    reverting.value = false
  }
}

async function handleDeleteChecked() {
  const ids = [...checkable.checkedIds.value]
  try {
    await Promise.all(ids.map((id) => deleteSop(id)))
  } catch (e) {
    toast.error(e instanceof Error ? e.message : '削除に失敗しました')
  } finally {
    checkable.clear()
    emit('data-changed')
  }
}
</script>

<style scoped>
.sop-panel {
  border-top: 4px solid var(--color-border);
  background: var(--color-surface);
}

.panel-header {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.5rem 1rem;
  border-bottom: 1px solid var(--color-border);
}

.panel-title {
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--color-text-heading);
}

.panel-count {
  font-size: 0.75rem;
  color: var(--color-text-faint);
}

.reorder-actions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-left: auto;
}

.reorder-btn {
  background: var(--color-accent-bg);
  color: var(--color-accent);
  border: 1px solid var(--color-border-strong);
  border-radius: 5px;
  padding: 0.25rem 0.6rem;
  font-size: 0.75rem;
  cursor: pointer;
  white-space: nowrap;
}

.reorder-btn:hover:not(:disabled) {
  background: var(--color-accent-bg-hover);
}

.reorder-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.dirty-hint {
  font-size: 0.72rem;
  color: var(--color-warning);
  white-space: nowrap;
}

.reorder-error {
  font-size: 0.72rem;
  color: var(--color-danger);
}

.checked-actions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.revert-btn {
  background: var(--color-accent-bg);
  color: var(--color-accent);
  border: 1px solid var(--color-border-strong);
  border-radius: 5px;
  padding: 0.25rem 0.6rem;
  font-size: 0.75rem;
  cursor: pointer;
  white-space: nowrap;
}

.revert-btn:hover:not(:disabled) {
  background: var(--color-accent-bg-hover);
}

.revert-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.delete-selected-btn {
  background: var(--color-danger-bg);
  color: var(--color-danger);
  border: 1px solid var(--color-danger-border);
  border-radius: 5px;
  padding: 0.25rem 0.6rem;
  font-size: 0.75rem;
  cursor: pointer;
  white-space: nowrap;
}

.sop-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.8rem;
}

.sop-table th,
.sop-table td {
  padding: 0.4rem 1rem;
  text-align: left;
  border-bottom: 1px solid var(--color-border);
  white-space: nowrap;
}

.sop-table th {
  color: var(--color-text-muted);
  font-weight: 600;
  font-size: 0.72rem;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.check-col,
.drag-col {
  width: 1.5rem;
  padding-left: 0.75rem !important;
  padding-right: 0 !important;
}

.cell-input {
  width: 100%;
  min-width: 4rem;
  background: var(--color-bg);
  color: var(--color-text);
  border: 1px solid var(--color-accent);
  border-radius: 4px;
  padding: 0.2rem 0.35rem;
  font-size: 0.8rem;
  font-family: inherit;
}

.drag-handle {
  color: var(--color-text-faint);
  cursor: grab;
}

.sop-row {
  color: var(--color-text);
  background: var(--color-surface);
  transition: background 0.15s;
}

.sop-row.dragging {
  opacity: 0.4;
}

.uid-cell {
  color: var(--color-text-muted);
  font-size: 0.75rem;
  max-width: 320px;
  overflow: hidden;
  text-overflow: ellipsis;
}
</style>
