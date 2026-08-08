<!--
  ======================================================
  SeriesListPanel.vue — 「シリーズリスト」パネル（インライン表示）
  ======================================================
  Vue版 frontend/src/features/study/components/SeriesListPanel.vue の移植。
  行を選択すると右側にサムネイル（そのシリーズの代表画像）を描画する。
  ダブルクリックで画像ビューアページを開く。
-->

<template>
  <section class="series-panel">
    <div class="panel-header">
      <span class="panel-title">▼ シリーズリスト</span>
      <span class="panel-count">件数：{{ study.series.length }}</span>
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

    <div class="panel-body">
      <table class="series-table">
        <thead>
          <tr>
            <th class="check-col" />
            <th class="drag-col" />
            <th>シリーズ番号</th>
            <th>モダリティ</th>
            <th>画像数</th>
            <th>シリーズ記述</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="(series, index) in editable.workingItems.value"
            :key="series.seriesInstanceUID"
            class="series-row"
            :class="{
              selected: series.seriesInstanceUID === selectedSeriesUID,
              dragging: draggingIndex === index,
            }"
            v-bind="dropTargetProps(index)"
            @click="selectSeries(series)"
            @dblclick="$emit('open-images', series)"
          >
            <td class="check-col" @click.stop>
              <input
                type="checkbox"
                :checked="checkable.isChecked(series)"
                @change="checkable.toggle(series)"
              />
            </td>
            <td class="drag-col" @click.stop>
              <span class="drag-handle" title="ドラッグで並べ替え" v-bind="dragHandleProps(index)">
                ⠿
              </span>
            </td>
            <td>
              <input
                v-if="checkable.isChecked(series)"
                v-model="series.seriesNumber"
                class="cell-input"
                @click.stop
              />
              <template v-else>{{ series.seriesNumber || '—' }}</template>
            </td>
            <td>
              <input
                v-if="checkable.isChecked(series)"
                v-model="series.modality"
                class="cell-input"
                @click.stop
              />
              <template v-else>{{ series.modality || '—' }}</template>
            </td>
            <td>{{ series.numberOfInstances }}</td>
            <td>
              <input
                v-if="checkable.isChecked(series)"
                v-model="series.seriesDescription"
                class="cell-input"
                @click.stop
              />
              <template v-else>{{ series.seriesDescription || '説明なし' }}</template>
            </td>
          </tr>
        </tbody>
      </table>

      <div class="thumbnail-preview">
        <canvas ref="canvasEl" class="preview-canvas" />
        <p v-if="!selectedSeries" class="preview-hint">シリーズを選択</p>
        <p v-else-if="thumbnailError" class="preview-error">画像を読み込めませんでした</p>
        <p class="preview-caption">ダブルクリックで画像を開く</p>
      </div>
    </div>

    <SopListPanel
      v-if="selectedSeries"
      :key="selectedSeries.seriesInstanceUID"
      :series="selectedSeries"
      @data-changed="$emit('data-changed')"
    />

    <ConfirmDialog
      v-model="showDeleteConfirm"
      title="シリーズの削除"
      confirm-text="削除する"
      @confirm="handleDeleteChecked"
    >
      選択した{{ checkable.checkedIds.value.size }}件のシリーズを削除します。
      DBのレコードと紐づくDICOM画像も削除され、元に戻せません。よろしいですか？
    </ConfirmDialog>
  </section>
</template>

<script setup lang="ts">
import type { DicomStudy, DicomSeries } from '~/types/dicom'
import type { SeriesChangeInput } from '~/utils/backendApiService'

const props = defineProps<{
  study: DicomStudy
}>()

const emit = defineEmits<{
  'open-images': [series: DicomSeries]
  'data-changed': []
}>()

const authStore = useAuthStore()
const toast = useToast()
const studySeries = computed(() => props.study.series)
const editable = useEditableList(studySeries, {
  getId: (s: DicomSeries) => s.seriesInstanceUID,
  getOrder: (s: DicomSeries) => s.order,
  getFields: (s: DicomSeries) => ({
    seriesNumber: s.seriesNumber,
    seriesDescription: s.seriesDescription,
    modality: s.modality,
  }),
})
const { draggingIndex, dragHandleProps, dropTargetProps } = useDragSort(editable.workingItems)

async function handleSave() {
  try {
    const count = await editable.save(
      (series, patch): SeriesChangeInput => ({
        seriesInstanceUid: series.seriesInstanceUID,
        order: patch.order,
        seriesNumber: patch.fields?.seriesNumber,
        seriesDescription: patch.fields?.seriesDescription,
        modality: patch.fields?.modality,
      }),
      saveSeriesChanges
    )
    if (count > 0) toast.success(`${count}件の変更を保存しました`)
  } catch (e) {
    toast.error(e instanceof Error ? e.message : '保存に失敗しました')
  }
}

const selectedSeries = ref<DicomSeries | null>(null)
const selectedSeriesUID = ref<string | null>(null)
const canvasEl = ref<HTMLCanvasElement | null>(null)
const thumbnailError = ref(false)

function selectSeries(series: DicomSeries) {
  selectedSeries.value = series
  selectedSeriesUID.value = series.seriesInstanceUID
}

// studySeries（親のstudy.series）が再取得等で新しい配列に置き換わったとき、
// 選択中シリーズがまだ存在するなら新しいオブジェクト参照に差し替える。
watch(studySeries, (series) => {
  if (!selectedSeriesUID.value) return
  selectedSeries.value = series.find((s) => s.seriesInstanceUID === selectedSeriesUID.value) ?? null
})

async function renderPreview() {
  thumbnailError.value = false
  const series = selectedSeries.value
  const canvas = canvasEl.value
  const firstInstance = series?.instances[0]
  if (!series || !canvas || !firstInstance) return

  try {
    await renderDicomToCanvas(firstInstance.filePath, canvas)
  } catch {
    thumbnailError.value = true
  }
}

watch(selectedSeries, renderPreview)

// パネルが表示されたら最初のシリーズを自動選択しておく。
onMounted(() => {
  // length > 0 を確認済みのため、noUncheckedIndexedAccessによる
  // `DicomSeries | undefined` 型を安全に確定させてよい。
  if (props.study.series.length > 0) {
    selectSeries(props.study.series[0]!)
  }
})

const checkable = useCheckableRows<DicomSeries>((s) => s.seriesInstanceUID)
const showDeleteConfirm = ref(false)
const reverting = ref(false)

async function handleRevertChecked() {
  const ids = [...checkable.checkedIds.value]
  reverting.value = true
  try {
    for (const id of ids) {
      const reverted = await revertSeriesFields(id)
      const series = studySeries.value.find((s) => s.seriesInstanceUID === id)
      if (!series) continue
      series.seriesNumber = reverted.seriesNumber
      series.seriesDescription = reverted.seriesDescription
      series.modality = reverted.modality
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
    await Promise.all(ids.map((id) => deleteSeries(id)))
  } catch (e) {
    toast.error(e instanceof Error ? e.message : '削除に失敗しました')
  } finally {
    checkable.clear()
    emit('data-changed')
  }
}
</script>

<style scoped>
.series-panel {
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

.check-col,
.drag-col {
  width: 1.5rem;
  padding-left: 0.75rem !important;
  padding-right: 0 !important;
}

.cell-input {
  width: 100%;
  min-width: 5rem;
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

.series-row.dragging {
  opacity: 0.4;
}

.panel-body {
  display: flex;
  align-items: stretch;
}

.series-table {
  flex: 1;
  border-collapse: collapse;
  font-size: 0.8rem;
  min-width: 0;
}

.series-table th,
.series-table td {
  padding: 0.4rem 1rem;
  text-align: left;
  border-bottom: 1px solid var(--color-border);
  white-space: nowrap;
}

.series-table th {
  color: var(--color-text-muted);
  font-weight: 600;
  font-size: 0.72rem;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.series-row {
  cursor: pointer;
  color: var(--color-text);
  background: var(--color-surface);
  transition: background 0.15s;
}

.series-row:hover {
  background: var(--color-surface-alt);
}

.series-row.selected {
  background: var(--color-accent-selected-bg);
  color: var(--color-accent);
}

.thumbnail-preview {
  position: relative;
  width: 160px;
  flex-shrink: 0;
  aspect-ratio: 1 / 1;
  background: var(--color-canvas-bg);
  border-left: 1px solid var(--color-border);
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
}

.preview-canvas {
  max-width: 100%;
  max-height: 100%;
}

.preview-hint,
.preview-error {
  position: absolute;
  font-size: 0.72rem;
  color: var(--color-text-disabled);
}

.preview-error {
  color: var(--color-danger);
}

.preview-caption {
  position: absolute;
  bottom: 2px;
  font-size: 0.62rem;
  color: var(--color-text-disabled);
}
</style>
