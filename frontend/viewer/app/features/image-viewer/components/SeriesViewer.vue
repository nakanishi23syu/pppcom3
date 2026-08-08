<!--
  ======================================================
  SeriesViewer.vue — DICOM画像ビューア
  ======================================================
  Vue版 frontend/src/features/image-viewer/components/SeriesViewer.vue の移植。
  Props にシリーズ（DicomSeries）だけを受け取り、「どのシリーズを開くか」の解決は
  呼び出し側の pages/viewer/[seriesInstanceUID].vue の責務とする。
-->

<template>
  <div class="series-viewer">
    <SeriesThumbnailPanel
      :instances="series.instances"
      :selected-index="selectedIndex"
      @select="selectedIndex = $event"
    />

    <SeriesMainStage
      :instance="currentInstance"
      :index="selectedIndex"
      :total="series.instances.length"
      @prev="selectedIndex--"
      @next="selectedIndex++"
    />
  </div>
</template>

<script setup lang="ts">
import type { DicomSeries } from '~/types/dicom'

const props = defineProps<{
  series: DicomSeries
}>()

const selectedIndex = ref(0)

const currentInstance = computed(() => props.series.instances[selectedIndex.value] ?? null)

watch(
  () => props.series.seriesInstanceUID,
  () => {
    selectedIndex.value = 0
  }
)
</script>

<style scoped>
.series-viewer {
  height: 100%;
  display: flex;
  overflow: hidden;
}
</style>
