<!--
  ======================================================
  PatientHistoryPanel.vue — 「同一患者IDの全検査」パネル
  ======================================================
  Vue版 frontend/src/features/study/components/PatientHistoryPanel.vue の移植。
  選択中の検査と同じ患者IDを持つ検査を、日付の新しい順に一覧表示する。
-->

<template>
  <section class="history-panel">
    <div class="panel-header">
      <span class="panel-title">▼ 同一患者IDの全検査</span>
      <span class="panel-patient">
        {{ selectedStudy.patientName }}　{{ selectedStudy.patientID }}
      </span>
      <span class="panel-count">件数：{{ historyStudies.length }}</span>
    </div>

    <table class="history-table">
      <thead>
        <tr>
          <th>患者ID</th>
          <th>患者氏名</th>
          <th>検査日時</th>
          <th>代表モダリティ</th>
          <th>全検査部位</th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="study in historyStudies"
          :key="study.studyInstanceUID"
          class="history-row"
          :class="{ selected: study.studyInstanceUID === selectedStudy.studyInstanceUID }"
          @click="$emit('select-study', study)"
        >
          <td>{{ study.patientID || '—' }}</td>
          <td>{{ study.patientName || '—' }}</td>
          <td>{{ formatDate(study.studyDate) }}</td>
          <td>{{ study.modality || '—' }}</td>
          <td>{{ bodyPartOf(study) || '—' }}</td>
        </tr>
      </tbody>
    </table>
  </section>
</template>

<script setup lang="ts">
import type { DicomStudy } from '~/types/dicom'

const props = defineProps<{
  studies: DicomStudy[]
  selectedStudy: DicomStudy
}>()

defineEmits<{
  'select-study': [study: DicomStudy]
}>()

const historyStudies = computed(() =>
  [...props.studies]
    .filter((s) => s.patientID === props.selectedStudy.patientID)
    .sort((a, b) => b.studyDate.localeCompare(a.studyDate))
)

function formatDate(raw: string): string {
  if (!raw || raw.length !== 8) return raw || '—'
  return `${raw.slice(0, 4)}/${raw.slice(4, 6)}/${raw.slice(6, 8)}`
}

function bodyPartOf(study: DicomStudy): string {
  return study.series[0]?.seriesDescription ?? ''
}
</script>

<style scoped>
.history-panel {
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

.panel-patient {
  font-size: 0.8rem;
  color: var(--color-text-muted);
}

.panel-count {
  margin-left: auto;
  font-size: 0.75rem;
  color: var(--color-text-faint);
}

.history-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.8rem;
}

.history-table th,
.history-table td {
  padding: 0.4rem 1rem;
  text-align: left;
  border-bottom: 1px solid var(--color-border);
  white-space: nowrap;
}

.history-table th {
  color: var(--color-text-muted);
  font-weight: 600;
  font-size: 0.72rem;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.history-row {
  cursor: pointer;
  color: var(--color-text);
  background: var(--color-surface);
  transition: background 0.15s;
}

.history-row:hover {
  background: var(--color-surface-alt);
}

.history-row.selected {
  background: var(--color-accent-selected-bg);
  color: var(--color-accent);
}
</style>
