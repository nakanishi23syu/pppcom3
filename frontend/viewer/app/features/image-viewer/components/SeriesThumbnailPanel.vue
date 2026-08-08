<!--
  ======================================================
  SeriesThumbnailPanel.vue — サムネイル一覧（左側パネル）
  ======================================================
  Vue版 frontend/src/features/image-viewer/components/SeriesThumbnailPanel.vue の移植。

  【WebGLコンテキストの節約について】
  dicom.ts の render() はcanvasごとにWebGLコンテキストを1つ消費する。
  ブラウザはページ全体で同時に持てるWebGLコンテキスト数に上限があるため、
  実際にWebGLで描画するcanvasは非表示の共有canvas1枚だけにし、各サムネイルは
  その描画結果を2D canvas（drawImage）でコピーするだけにする。
-->

<template>
  <aside class="thumb-panel">
    <p class="thumb-panel-title">{{ instances.length }} 枚</p>

    <canvas ref="sharedRenderCanvas" class="shared-render-canvas" />

    <div class="thumb-list">
      <button
        v-for="(instance, idx) in instances"
        :key="instance.sopInstanceUID || idx"
        type="button"
        class="thumb-item"
        :class="{ active: idx === selectedIndex }"
        @click="$emit('select', idx)"
      >
        <div class="thumb-canvas-wrap">
          <canvas
            :ref="(el) => enqueueThumbnail(el as HTMLCanvasElement | null, instance.filePath)"
          />
          <div v-if="thumbErrors[instance.filePath]" class="thumb-error">✕</div>
        </div>
        <span class="thumb-label">#{{ instance.instanceNumber || idx + 1 }}</span>
      </button>
    </div>
  </aside>
</template>

<script setup lang="ts">
import type { DicomInstance } from '~/types/dicom'

defineProps<{
  instances: DicomInstance[]
  selectedIndex: number
}>()

defineEmits<{
  select: [index: number]
}>()

const thumbErrors = reactive<Record<string, boolean>>({})

// WebGL描画を行う唯一の共有canvas。
const sharedRenderCanvas = ref<HTMLCanvasElement | null>(null)

// v-forの各canvasは:refのコールバックでほぼ同時にマウントされるため、
// Promiseチェーンで「前の描画が終わってから次を描画する」直列実行のキューを作る。
let renderQueue: Promise<void> = Promise.resolve()

function enqueueThumbnail(canvas: HTMLCanvasElement | null, filePath: string) {
  if (!canvas) return
  renderQueue = renderQueue.then(() => renderThumbnail(canvas, filePath))
}

async function renderThumbnail(canvas: HTMLCanvasElement, filePath: string) {
  const shared = sharedRenderCanvas.value
  if (!shared) return

  try {
    await renderDicomToCanvas(filePath, shared)
    canvas.width = shared.width
    canvas.height = shared.height
    canvas.getContext('2d')?.drawImage(shared, 0, 0)
  } catch {
    thumbErrors[filePath] = true
  }
}
</script>

<style scoped>
.thumb-panel {
  width: 220px;
  flex-shrink: 0;
  border-right: 1px solid var(--color-border);
  background: var(--color-bg);
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.thumb-panel-title {
  padding: 0.75rem 1rem 0.5rem;
  font-size: 0.75rem;
  color: var(--color-text-muted);
  flex-shrink: 0;
}

.shared-render-canvas {
  position: absolute;
  visibility: hidden;
  pointer-events: none;
}

.thumb-list {
  flex: 1;
  overflow-y: auto;
  padding: 0 0.75rem 0.75rem;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.thumb-item {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 6px;
  padding: 0.4rem;
  cursor: pointer;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.3rem;
  transition:
    border-color 0.15s,
    background 0.15s;
}

.thumb-item:hover {
  border-color: var(--color-thumbnail-selected-border);
  background: var(--color-thumbnail-selected-bg);
}

.thumb-item.active {
  border-color: var(--color-accent);
  background: var(--color-accent-selected-bg);
}

.thumb-canvas-wrap {
  position: relative;
  width: 100%;
  aspect-ratio: 1 / 1;
  background: var(--color-canvas-bg);
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
  border-radius: 4px;
}

.thumb-canvas-wrap canvas {
  max-width: 100%;
  max-height: 100%;
}

.thumb-error {
  position: absolute;
  color: var(--color-danger);
  font-size: 0.7rem;
}

.thumb-label {
  font-size: 0.7rem;
  color: var(--color-text-muted);
}
</style>
