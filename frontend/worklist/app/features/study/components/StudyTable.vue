<!--
  ======================================================
  StudyTable.vue — 検査一覧テーブルコンポーネント
  ======================================================
  dicom-tool-2/nuxt からの移植。フィルター/ソート/並べ替え/インライン編集/チェック削除の
  仕組みはすべて元の実装と同じcomposableを使い回している。

  並べ替え・ソートはNotionのテーブルビューの操作感に合わせている:
    - 列見出し（カラム名）をクリックするとその列でソートされる（昇順→降順→解除の順に切り替え）
    - ソートが無効な間は、行の左端のハンドル（⠿）をドラッグして手動で並べ替えられる

  【ドラッグの起点について（composables/useDragSort.ts参照）】
  dragHandleProps(index) は必ずハンドル要素（span.drag-handle）にだけ付け、行(<tr>)自体には
  付けない。dropTargetProps(index) は行(<tr>)に付ける。この2分割を守らないと、
  セル編集用の<input>をドラッグしようとしただけで並べ替えが暴発してしまう。

  【新規機能: 右クリックで「タイムラインを開く」（サービス分離に伴う追加）】
  元のdicom-tool-2/nuxtでは「🕐 比較読影」列がアプリ内蔵の pages/timeline/[patientId].vue への
  NuxtLinkだったが、Timelineは別プロセス・別ポート(5230)のBlazor WebAssemblyアプリとして
  分離されたため、Nuxtの内部ルーティングでは開けなくなった。ブラウザから直接window.openで
  別オリジンを開くことも可能だが、このプロジェクトでは学習目的であえて「常駐トレイアプリ
  (services/DicomTool.TrayApp, ポート5299)にHTTP POSTで命令し、OSネイティブの機能で
  ブラウザを開いてもらう」という実務のデスクトップ連携に近い構図を採用している
  （詳しくは utils/trayAppService.ts のコメントを参照）。
  行を右クリックするとコンテキストメニューが出て「タイムラインを開く」を選べるほか、
  従来の列にも同じ操作を行うボタンを残している（右クリックに気づかないユーザーのための保険）。
-->

<template>
  <div class="study-table-wrap">
    <div class="filter-sort-toolbar">
      <div class="toolbar-buttons">
        <BaseButton variant="secondary" @click="showFilterPanel = !showFilterPanel">
          🔍 フィルター
          <span v-if="filterRules.length">（{{ filterRules.length }}）</span>
        </BaseButton>
        <span v-if="sort" class="sort-status">
          ↕ {{ FIELD_LABELS[sort.field] }}で{{ sort.direction === 'asc' ? '昇順' : '降順' }}ソート中
          <button class="sort-clear" @click="sort = null">解除</button>
        </span>
        <span v-else class="sort-hint">列見出しをクリックするとソートできます</span>

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
          <span v-if="editable.dirty.value" class="dirty-hint">未保存の変更があります</span>
          <span v-if="!authStore.isAdmin" class="sort-hint">
            ※並べ替えの保存は管理者のみ反映されます
          </span>
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

      <div v-if="showFilterPanel" class="rule-panel filter-panel">
        <div v-for="rule in filterRules" :key="rule.id" class="rule-row">
          <select v-model="rule.field" class="rule-select" @change="onFilterFieldChange(rule)">
            <option v-for="f in FILTER_FIELDS" :key="f" :value="f">{{ FIELD_LABELS[f] }}</option>
          </select>
          <select v-model="rule.operator" class="rule-select">
            <option v-for="op in operatorsForField(rule.field)" :key="op" :value="op">
              {{ OPERATOR_LABELS[op] }}
            </option>
          </select>
          <input
            v-if="operatorNeedsValue(rule.operator) && rule.field !== 'studyDate'"
            v-model="rule.value"
            class="rule-value"
            type="text"
            placeholder="値を入力"
          />
          <input
            v-else-if="operatorNeedsValue(rule.operator)"
            class="rule-value"
            type="date"
            :value="toInputDate(rule.value)"
            @input="rule.value = fromInputDate(($event.target as HTMLInputElement).value)"
          />
          <button class="rule-remove" aria-label="削除" @click="removeFilterRule(rule.id)">
            ✕
          </button>
        </div>
        <button class="rule-add" @click="addFilterRule('patientName')">+ フィルターを追加</button>
      </div>
    </div>

    <div v-if="loading" class="state-msg">
      <span class="spinner" />
      読み込み中...
    </div>

    <div v-else-if="error" class="state-msg error">{{ error }}</div>

    <div v-else-if="studies.length === 0" class="state-msg">
      <p>検査データがありません。</p>
      <p class="hint">/upload からDICOMファイルをアップロードしてください。</p>
    </div>

    <div v-else-if="displayedStudies.length === 0" class="state-msg">
      <p>フィルター条件に一致する検査がありません。</p>
    </div>

    <table v-else class="study-table">
      <thead>
        <tr>
          <th class="check-col" />
          <th class="drag-col" />
          <th class="sortable" @click="toggleSort('patientID')">
            患者ID
            <span v-if="sort?.field === 'patientID'" class="sort-arrow">{{ sortArrow }}</span>
          </th>
          <th>読影ステータス</th>
          <th class="sortable" @click="toggleSort('patientName')">
            患者氏名
            <span v-if="sort?.field === 'patientName'" class="sort-arrow">{{ sortArrow }}</span>
          </th>
          <th class="sortable" @click="toggleSort('studyDate')">
            検査日時
            <span v-if="sort?.field === 'studyDate'" class="sort-arrow">{{ sortArrow }}</span>
          </th>
          <th class="sortable" @click="toggleSort('modality')">
            代表モダリティ
            <span v-if="sort?.field === 'modality'" class="sort-arrow">{{ sortArrow }}</span>
          </th>
          <th>全検査部位</th>
          <th class="sortable" @click="toggleSort('studyDescription')">
            検査記述
            <span v-if="sort?.field === 'studyDescription'" class="sort-arrow">
              {{ sortArrow }}
            </span>
          </th>
          <th>シリーズ数</th>
          <th>タイムライン</th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="(study, index) in displayedStudies"
          :key="study.studyInstanceUID"
          class="study-row"
          :class="{
            selected: selectedUID === study.studyInstanceUID,
            dragging: !sort && draggingIndex === index,
          }"
          v-bind="!sort ? dropTargetProps(index) : {}"
          @click="$emit('select-study', study)"
          @contextmenu.prevent.stop="openContextMenu($event, study)"
        >
          <td class="check-col" @click.stop>
            <input
              type="checkbox"
              :checked="checkable.isChecked(study)"
              @change="checkable.toggle(study)"
            />
          </td>
          <td class="drag-col" :class="{ 'drag-disabled': !!sort }" @click.stop>
            <span
              class="drag-handle"
              :title="sort ? 'ソート解除で手動並べ替え可能' : 'ドラッグで並べ替え'"
              v-bind="!sort ? dragHandleProps(index) : {}"
            >
              ⠿
            </span>
          </td>
          <td>
            <input
              v-if="checkable.isChecked(study)"
              v-model="study.patientID"
              class="cell-input"
              @click.stop
            />
            <template v-else>{{ study.patientID || '—' }}</template>
          </td>
          <td>
            <ReadingStatusBadge
              :status="getStatus(study.studyInstanceUID)"
              @click.stop
              @update:status="setStatus(study.studyInstanceUID, $event)"
            />
          </td>
          <td>
            <input
              v-if="checkable.isChecked(study)"
              v-model="study.patientName"
              class="cell-input"
              @click.stop
            />
            <template v-else>{{ study.patientName || '—' }}</template>
          </td>
          <td>
            <input
              v-if="checkable.isChecked(study)"
              class="cell-input"
              type="date"
              :value="toInputDate(study.studyDate)"
              @click.stop
              @input="study.studyDate = fromInputDate(($event.target as HTMLInputElement).value)"
            />
            <template v-else>{{ formatDate(study.studyDate) }}</template>
          </td>
          <td>
            <input
              v-if="checkable.isChecked(study)"
              v-model="study.modality"
              class="cell-input"
              @click.stop
            />
            <span v-else class="badge">{{ study.modality || '—' }}</span>
          </td>
          <td>{{ allBodyParts(study) }}</td>
          <td>
            <input
              v-if="checkable.isChecked(study)"
              v-model="study.studyDescription"
              class="cell-input"
              @click.stop
            />
            <template v-else>{{ study.studyDescription || '—' }}</template>
          </td>
          <td>{{ study.series.length }}</td>
          <td>
            <!--
              Timelineは別プロセス(Blazor/5230)に分離されたため、内部ルート(NuxtLink)ではなく
              TrayApp経由でTimelineを開くボタンにしている（詳しくは上部のコメント・
              utils/trayAppService.ts を参照）。行の右クリックからも同じ操作ができる。
            -->
            <button
              v-if="study.patientID"
              type="button"
              class="timeline-link"
              @click.stop="handleOpenTimeline(study)"
            >
              🕐 タイムラインを開く
            </button>
            <span v-else class="timeline-link disabled">—</span>
          </td>
        </tr>
      </tbody>
    </table>

    <!--
      行を右クリックしたときのコンテキストメニュー。overlayをクリック/右クリックすると閉じる
      （BaseModal.vueのoverlay@click.selfと同じ「メニュー外クリックで閉じる」パターン）。
    -->
    <div
      v-if="contextMenu.visible"
      class="context-menu-overlay"
      @click="closeContextMenu"
      @contextmenu.prevent="closeContextMenu"
    >
      <div
        class="context-menu"
        :style="{ top: `${contextMenu.y}px`, left: `${contextMenu.x}px` }"
        @click.stop
      >
        <button type="button" class="context-menu-item" @click="handleOpenTimelineFromMenu">
          🕐 タイムラインを開く
        </button>
      </div>
    </div>

    <ConfirmDialog
      v-model="showDeleteConfirm"
      title="検査の削除"
      confirm-text="削除する"
      @confirm="handleDeleteChecked"
    >
      選択した{{ checkable.checkedIds.value.size }}件の検査を削除します。
      DBのレコードと紐づくDICOM画像も削除され、元に戻せません。よろしいですか？
    </ConfirmDialog>
  </div>
</template>

<script setup lang="ts">
import type { DicomStudy } from '~/types/dicom'
import type { FilterOperator, FilterRule } from '~/composables/useFilterSort'
import type { StudyChangeInput } from '~/utils/backendApiService'

const props = defineProps<{
  studies: DicomStudy[]
  loading: boolean
  error: string | null
  selectedUID: string | null
}>()

const emit = defineEmits<{
  'select-study': [study: DicomStudy]
  'data-changed': []
}>()

function formatDate(raw: string): string {
  if (!raw || raw.length !== 8) return raw || '—'
  return `${raw.slice(0, 4)}/${raw.slice(4, 6)}/${raw.slice(6, 8)}`
}

type StudyFilterField =
  'patientName' | 'patientID' | 'studyDate' | 'studyDescription' | 'modality' | 'accessionNumber'

const FILTER_FIELDS: StudyFilterField[] = [
  'patientName',
  'patientID',
  'studyDate',
  'studyDescription',
  'modality',
  'accessionNumber',
]

const FIELD_LABELS: Record<StudyFilterField, string> = {
  patientName: '患者氏名',
  patientID: '患者ID',
  studyDate: '検査日時',
  studyDescription: '検査記述',
  modality: '代表モダリティ',
  accessionNumber: 'アクセッション番号',
}

const OPERATOR_LABELS: Record<FilterOperator, string> = {
  contains: '含む',
  notContains: '含まない',
  equals: 'と等しい',
  isEmpty: '空である',
  isNotEmpty: '空でない',
  onOrAfter: '以降',
  onOrBefore: '以前',
}

function operatorsForField(field: StudyFilterField): FilterOperator[] {
  if (field === 'studyDate') {
    return ['onOrAfter', 'onOrBefore', 'equals', 'isEmpty', 'isNotEmpty']
  }
  return ['contains', 'notContains', 'equals', 'isEmpty', 'isNotEmpty']
}

function onFilterFieldChange(rule: FilterRule<StudyFilterField>) {
  const allowed = operatorsForField(rule.field)
  // allowedはoperatorsForField()が必ず要素5個以上の配列を返す実装のため空にはならないが、
  // noUncheckedIndexedAccessの都合上、添字アクセスは念のため確定させておく。
  if (!allowed.includes(rule.operator)) {
    rule.operator = allowed[0]!
  }
}

function toInputDate(dicomDate: string): string {
  if (dicomDate.length !== 8) return ''
  return `${dicomDate.slice(0, 4)}-${dicomDate.slice(4, 6)}-${dicomDate.slice(6, 8)}`
}
function fromInputDate(inputDate: string): string {
  return inputDate.split('-').join('')
}

function getFieldValue(study: DicomStudy, field: StudyFilterField): string {
  return study[field] ?? ''
}

function allBodyParts(study: DicomStudy): string {
  const parts = [...new Set(study.series.map((s) => s.seriesDescription).filter(Boolean))]
  return parts.length > 0 ? parts.join(' / ') : '—'
}

const { getStatus, setStatus } = useReadingStatus()

const {
  filterRules,
  sort,
  filteredItems,
  filteredSortedItems,
  addFilterRule,
  removeFilterRule,
  toggleSort,
} = useFilterSort<DicomStudy, StudyFilterField>(toRef(props, 'studies'), getFieldValue)

const showFilterPanel = ref(false)

const sortArrow = computed(() => (sort.value?.direction === 'asc' ? '▲' : '▼'))

const authStore = useAuthStore()
const toast = useToast()

// ======================================================
// 右クリックコンテキストメニュー「タイムラインを開く」
// ======================================================
// contextMenu.study を保持しておき、メニューの項目がクリックされたタイミングで
// TrayApp(services/DicomTool.TrayApp, ポート5299)へ命令を送る。
const contextMenu = reactive({
  visible: false,
  x: 0,
  y: 0,
  study: null as DicomStudy | null,
})

function openContextMenu(event: MouseEvent, study: DicomStudy) {
  contextMenu.visible = true
  contextMenu.x = event.clientX
  contextMenu.y = event.clientY
  contextMenu.study = study
}

function closeContextMenu() {
  contextMenu.visible = false
  contextMenu.study = null
}

// TrayAppへ「この患者のタイムラインを開いて」と命令する。
// TrayAppが起動していない場合はfetch自体が失敗する（openTimeline内でTrayAppUnreachableErrorに
// 変換される）ため、ここでは「常駐アプリが起動していません」等のメッセージをトーストで表示する。
async function handleOpenTimeline(study: DicomStudy) {
  if (!study.patientID) {
    toast.error('患者IDが空のためタイムラインを開けません')
    return
  }
  try {
    await openTimeline(study.patientID)
  } catch (e) {
    toast.error(
      e instanceof TrayAppUnreachableError
        ? e.message
        : e instanceof Error
          ? e.message
          : 'タイムラインを開けませんでした'
    )
  }
}

async function handleOpenTimelineFromMenu() {
  const study = contextMenu.study
  closeContextMenu()
  if (study) await handleOpenTimeline(study)
}

const editable = useEditableList(filteredItems, {
  getId: (s: DicomStudy) => s.studyInstanceUID,
  getOrder: (s: DicomStudy) => s.order,
  getFields: (s: DicomStudy) => ({
    patientId: s.patientID,
    patientName: s.patientName,
    studyDate: s.studyDate,
    studyDescription: s.studyDescription,
    modality: s.modality,
  }),
})

const { draggingIndex, dragHandleProps, dropTargetProps } = useDragSort(editable.workingItems)

async function handleSave() {
  try {
    const count = await editable.save(
      (study, patch): StudyChangeInput => ({
        studyInstanceUid: study.studyInstanceUID,
        order: patch.order,
        patientId: patch.fields?.patientId,
        patientName: patch.fields?.patientName,
        studyDate: patch.fields?.studyDate ? toInputDate(patch.fields.studyDate) : undefined,
        studyDescription: patch.fields?.studyDescription,
        modality: patch.fields?.modality,
      }),
      saveStudyChanges
    )
    if (count > 0) toast.success(`${count}件の変更を保存しました`)
  } catch (e) {
    toast.error(e instanceof Error ? e.message : '保存に失敗しました')
  }
}

const displayedStudies = computed(() =>
  sort.value ? filteredSortedItems.value : editable.workingItems.value
)

const checkable = useCheckableRows<DicomStudy>((s) => s.studyInstanceUID)
const showDeleteConfirm = ref(false)
const reverting = ref(false)

async function handleRevertChecked() {
  const ids = [...checkable.checkedIds.value]
  reverting.value = true
  try {
    for (const id of ids) {
      const reverted = await revertStudyFields(id)
      const study = props.studies.find((s) => s.studyInstanceUID === id)
      if (!study) continue
      study.patientID = reverted.patientId
      study.patientName = reverted.patientName
      study.studyDate = reverted.studyDate.split('-').join('')
      study.studyDescription = reverted.studyDescription
      study.modality = reverted.modality
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
    await Promise.all(ids.map((id) => deleteStudy(id)))
  } catch (e) {
    toast.error(e instanceof Error ? e.message : '削除に失敗しました')
  } finally {
    checkable.clear()
    emit('data-changed')
  }
}
</script>

<style scoped>
.filter-sort-toolbar {
  padding: 0.75rem 1rem;
  border-bottom: 1px solid var(--color-border);
}

.toolbar-buttons {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.sort-status {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  font-size: 0.8rem;
  color: var(--color-accent);
}

.sort-clear {
  background: none;
  border: none;
  color: var(--color-text-muted);
  font-size: 0.75rem;
  cursor: pointer;
  text-decoration: underline;
  padding: 0;
}

.sort-hint {
  font-size: 0.76rem;
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
  padding: 0.3rem 0.65rem;
  font-size: 0.78rem;
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
  font-size: 0.75rem;
  color: var(--color-warning);
  white-space: nowrap;
}

.reorder-error {
  font-size: 0.75rem;
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
  padding: 0.3rem 0.65rem;
  font-size: 0.78rem;
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
  padding: 0.3rem 0.65rem;
  font-size: 0.78rem;
  cursor: pointer;
  white-space: nowrap;
}

.rule-panel {
  margin-top: 0.6rem;
  padding: 0.75rem;
  background: var(--color-surface-alt);
  border: 1px solid var(--color-border);
  border-radius: 8px;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.rule-row {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.rule-select,
.rule-value {
  background: var(--color-bg);
  color: var(--color-text);
  border: 1px solid var(--color-border-strong);
  border-radius: 5px;
  padding: 0.3rem 0.5rem;
  font-size: 0.8rem;
}

.rule-value {
  flex: 1;
  min-width: 0;
}

.rule-remove {
  background: none;
  border: none;
  color: var(--color-text-muted);
  cursor: pointer;
  flex-shrink: 0;
}

.rule-remove:hover {
  color: var(--color-danger);
}

.rule-add {
  align-self: flex-start;
  background: none;
  border: none;
  color: var(--color-accent);
  font-size: 0.8rem;
  cursor: pointer;
  padding: 0.2rem 0;
}

.rule-add:hover {
  text-decoration: underline;
}

.study-table-wrap {
  width: 100%;
  overflow-x: auto;
}

.study-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.875rem;
}

.study-table thead tr {
  background: var(--color-border);
  color: var(--color-text-muted);
  text-transform: uppercase;
  font-size: 0.75rem;
  letter-spacing: 0.05em;
}

.study-table th.sortable {
  cursor: pointer;
  user-select: none;
}

.study-table th.sortable:hover {
  color: var(--color-accent);
}

.sort-arrow {
  margin-left: 0.25rem;
  font-size: 0.65rem;
}

.check-col,
.drag-col {
  width: 1.5rem;
  padding-left: 0.75rem !important;
  padding-right: 0 !important;
}

.cell-input {
  width: 100%;
  min-width: 6rem;
  background: var(--color-bg);
  color: var(--color-text);
  border: 1px solid var(--color-accent);
  border-radius: 4px;
  padding: 0.25rem 0.4rem;
  font-size: 0.85rem;
  font-family: inherit;
}

.study-table th,
.study-table td {
  padding: 0.625rem 1rem;
  text-align: left;
  border-bottom: 1px solid var(--color-border);
  white-space: nowrap;
}

.drag-handle {
  color: var(--color-text-faint);
  cursor: grab;
}

.drag-col.drag-disabled .drag-handle {
  color: var(--color-text-disabled);
  cursor: not-allowed;
}

.study-row {
  cursor: pointer;
  color: var(--color-text);
  background: var(--color-surface);
  transition: background 0.15s;
}

.study-row:hover {
  background: var(--color-thumbnail-selected-bg);
}

.study-row.dragging {
  opacity: 0.4;
}

.study-row.selected {
  background: var(--color-accent-selected-bg);
  color: var(--color-accent);
}

.badge {
  display: inline-block;
  background: var(--color-border-strong);
  color: var(--color-accent);
  border-radius: 4px;
  padding: 1px 6px;
  font-size: 0.75rem;
  font-weight: 600;
}

.timeline-link {
  background: none;
  border: none;
  font-family: inherit;
  font-size: 0.78rem;
  color: var(--color-accent);
  text-decoration: none;
  white-space: nowrap;
  cursor: pointer;
  padding: 0;
}

.timeline-link:hover {
  text-decoration: underline;
}

.timeline-link.disabled {
  color: var(--color-text-faint);
  cursor: default;
}

.context-menu-overlay {
  position: fixed;
  inset: 0;
  z-index: 300;
}

.context-menu {
  position: fixed;
  min-width: 180px;
  background: var(--color-surface);
  border: 1px solid var(--color-border-strong);
  border-radius: 6px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.35);
  padding: 0.3rem;
}

.context-menu-item {
  display: block;
  width: 100%;
  text-align: left;
  background: none;
  border: none;
  color: var(--color-text);
  font-size: 0.82rem;
  padding: 0.45rem 0.6rem;
  border-radius: 4px;
  cursor: pointer;
}

.context-menu-item:hover {
  background: var(--color-accent-bg);
  color: var(--color-accent);
}

.state-msg {
  padding: 3rem;
  text-align: center;
  color: var(--color-text-muted);
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.5rem;
}

.state-msg.error {
  color: var(--color-danger);
}

.hint {
  font-size: 0.8rem;
  color: var(--color-text-disabled);
}

.spinner {
  display: inline-block;
  width: 20px;
  height: 20px;
  border: 2px solid var(--color-border-strong);
  border-top-color: var(--color-accent);
  border-radius: 50%;
  animation: spin 0.7s linear infinite;
  margin-bottom: 0.5rem;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}
</style>
