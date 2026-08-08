<!--
  ======================================================
  pages/upload.vue — DICOMアップロードページ
  ======================================================
  dicom-tool-2/nuxt からの移植（ロジック・見た目は完全に同一）。
  検査・シリーズ・SOPの各テーブルへのインサートと、DICOMファイル本体の保存は
  すべてBackend API（backend/DicomTool.Api、ポート5030）側で行う。このページの役割は
  「ユーザーにファイルを選んでもらい、アップロードボタンでBackend APIへ送る」ことだけ。

  Backend API側は uploadDicomFiles という GraphQL Mutation（graphql-multipart-request-spec準拠の
  マルチパートアップロード）で実装されている。詳しい組み立て方は utils/uploadService.ts を参照。
  docs/CONTRACT.md 9章にある通り、この経路（旧アップロード経路）は内部的にはステージング領域への
  書き込み→Temporalワークフロー起動という形にBackend側で変更されているが、フロントエンドから見た
  インターフェース（GraphQL Mutation）自体は変わっていない。

  ファイルの選び方は4通り:
    1. ドラッグ&ドロップ（ファイル） 2. ドラッグ&ドロップ（フォルダ）
    3. 「ファイルを選択」ボタン        4. 「フォルダを選択」ボタン
-->

<template>
  <div class="page">
    <header class="page-header">
      <NuxtLink to="/" class="back-link">← 検査一覧に戻る</NuxtLink>
      <h1>DICOMアップロード</h1>
      <p class="page-desc">
        ファイル・フォルダをドラッグ&ドロップするか、下のボタンから選択してください。
        アップロードボタンを押すまでBackend APIへは送信されません。
      </p>
    </header>

    <main class="page-main">
      <div
        class="dropzone"
        :class="{ 'is-dragover': isDragOver }"
        @dragover.prevent="isDragOver = true"
        @dragleave.prevent="isDragOver = false"
        @drop.prevent="handleDrop"
      >
        <p class="dropzone-text">ここにDICOMファイル（またはフォルダ）をドラッグ&ドロップ</p>
        <div class="dropzone-buttons">
          <BaseButton variant="secondary" @click="fileInput?.click()">ファイルを選択</BaseButton>
          <BaseButton variant="secondary" @click="folderInput?.click()">フォルダを選択</BaseButton>
        </div>
        <input
          ref="fileInput"
          type="file"
          multiple
          class="hidden-input"
          @change="handleFileInputChange"
        />
        <input
          ref="folderInput"
          type="file"
          multiple
          webkitdirectory
          class="hidden-input"
          @change="handleFileInputChange"
        />
      </div>

      <section v-if="selectedFiles.length > 0" class="list-section">
        <div class="list-header">
          <h3>選択中のファイル（{{ selectedFiles.length }}件）</h3>
          <div class="control-row">
            <CancelButton @click="clearFiles">クリア</CancelButton>
            <SaveButton :disabled="uploading" @click="handleUpload">
              {{ uploading ? 'アップロード中…' : 'アップロード' }}
            </SaveButton>
          </div>
        </div>
        <ul class="file-list">
          <li v-for="(file, index) in selectedFiles" :key="`${file.name}-${index}`" class="file-row">
            <span class="file-name">{{ file.name }}</span>
            <span class="file-size">{{ formatFileSize(file.size) }}</span>
            <button class="remove-btn" aria-label="削除" @click="removeFile(index)">✕</button>
          </li>
        </ul>
      </section>

      <section v-if="results.length > 0" class="list-section">
        <h3>アップロード結果</h3>
        <ul class="result-list">
          <li
            v-for="result in results"
            :key="result.fileName"
            class="result-row"
            :class="{ failed: !result.accepted }"
          >
            <span class="result-icon">{{ result.accepted ? '✓' : '✕' }}</span>
            <span class="result-name">{{ result.fileName }}</span>
            <span class="result-message">{{ result.message }}</span>
          </li>
        </ul>
      </section>
    </main>
  </div>
</template>

<script setup lang="ts">
import type { DicomUploadResult } from '~/utils/uploadService'

const fileInput = ref<HTMLInputElement | null>(null)
const folderInput = ref<HTMLInputElement | null>(null)

const selectedFiles = ref<File[]>([])
const isDragOver = ref(false)
const uploading = ref(false)
const results = ref<DicomUploadResult[]>([])

function addFiles(files: File[]) {
  selectedFiles.value = [...selectedFiles.value, ...files]
}

function removeFile(index: number) {
  selectedFiles.value = selectedFiles.value.filter((_, i) => i !== index)
}

function clearFiles() {
  selectedFiles.value = []
  results.value = []
}

function handleFileInputChange(event: Event) {
  const input = event.target as HTMLInputElement
  if (input.files) {
    addFiles(Array.from(input.files))
  }
  input.value = ''
}

async function handleDrop(event: DragEvent) {
  isDragOver.value = false
  if (!event.dataTransfer) return
  const files = await collectFilesFromDataTransfer(event.dataTransfer)
  addFiles(files)
}

async function handleUpload() {
  uploading.value = true
  try {
    results.value = await uploadDicomFiles(selectedFiles.value)
    const succeededNames = new Set(
      results.value.filter((r) => r.accepted).map((r) => r.fileName)
    )
    selectedFiles.value = selectedFiles.value.filter((f) => !succeededNames.has(f.name))
  } catch (e) {
    results.value = [
      {
        fileName: '(全体)',
        workflowId: '',
        accepted: false,
        message: e instanceof Error ? e.message : String(e),
      },
    ]
  } finally {
    uploading.value = false
  }
}

function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}
</script>

<style scoped>
.page {
  min-height: 100vh;
  display: flex;
  flex-direction: column;
}

.page-header {
  padding: 1.25rem 1.5rem 1rem;
  border-bottom: 1px solid var(--color-border);
  background: var(--color-surface);
}

.back-link {
  display: inline-block;
  font-size: 0.8rem;
  color: var(--color-accent);
  text-decoration: none;
  margin-bottom: 0.5rem;
}

.back-link:hover {
  text-decoration: underline;
}

.page-header h1 {
  margin: 0;
  font-size: 1.2rem;
  color: var(--color-text-heading);
}

.page-desc {
  margin: 0.35rem 0 0;
  font-size: 0.85rem;
  color: var(--color-text-muted);
}

.page-main {
  flex: 1;
  padding: 1.5rem;
  max-width: 720px;
  width: 100%;
  margin: 0 auto;
}

.dropzone {
  border: 2px dashed var(--color-border-strong);
  border-radius: 10px;
  padding: 2.5rem 1.5rem;
  text-align: center;
  transition:
    border-color 0.15s,
    background 0.15s;
}

.dropzone.is-dragover {
  border-color: var(--color-accent);
  background: var(--color-accent-bg);
}

.dropzone-text {
  margin: 0 0 1rem;
  font-size: 0.9rem;
  color: var(--color-text-muted);
}

.dropzone-buttons {
  display: flex;
  justify-content: center;
  gap: 0.75rem;
}

.hidden-input {
  display: none;
}

.list-section {
  margin-top: 1.5rem;
}

.list-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 0.5rem;
}

.list-header h3 {
  margin: 0;
  font-size: 0.85rem;
  color: var(--color-text-heading);
}

.control-row {
  display: flex;
  gap: 0.5rem;
}

.file-list,
.result-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
  max-height: 320px;
  overflow-y: auto;
}

.file-row {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  background: var(--color-bg);
  border: 1px solid var(--color-border);
  border-radius: 6px;
  padding: 0.45rem 0.7rem;
  font-size: 0.8rem;
}

.file-name {
  flex: 1;
  color: var(--color-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.file-size {
  color: var(--color-text-faint);
  font-size: 0.72rem;
  flex-shrink: 0;
}

.remove-btn {
  background: none;
  border: none;
  color: var(--color-text-muted);
  cursor: pointer;
  padding: 0 0.2rem;
  flex-shrink: 0;
}

.remove-btn:hover {
  color: var(--color-danger);
}

.result-row {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  background: var(--color-bg);
  border: 1px solid var(--color-success-border);
  border-radius: 6px;
  padding: 0.45rem 0.7rem;
  font-size: 0.8rem;
}

.result-row.failed {
  border-color: var(--color-danger-border);
}

.result-icon {
  color: var(--color-success);
  flex-shrink: 0;
}

.result-row.failed .result-icon {
  color: var(--color-danger);
}

.result-name {
  color: var(--color-text);
  flex-shrink: 0;
  max-width: 40%;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.result-message {
  color: var(--color-text-muted);
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
