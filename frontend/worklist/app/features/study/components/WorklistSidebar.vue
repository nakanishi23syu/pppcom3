<!--
  ======================================================
  WorklistSidebar.vue — 検査一覧の左サイドバー（検索プリセット / 分類フォルダ）
  ======================================================
  Vue版 frontend/src/features/study/components/WorklistSidebar.vue の移植。
  「検索条件を保存する」「フォルダに振り分ける」機能自体は無いため、見た目の再現にとどめる。
  唯一 "全体" だけは実際にフィルターのクリアと連動させる。
-->

<template>
  <aside class="sidebar">
    <section class="tree-section">
      <button class="tree-heading" @click="filterPresetsOpen = !filterPresetsOpen">
        <span class="tree-caret" :class="{ open: filterPresetsOpen }">▶</span>
        検索プリセット
      </button>
      <ul v-if="filterPresetsOpen" class="tree-list">
        <li
          v-for="item in presetItems"
          :key="item"
          class="tree-item"
          :class="{ active: item === '全体' }"
          @click="$emit('select-preset', item)"
        >
          {{ item }}
        </li>
      </ul>
    </section>

    <section class="tree-section">
      <button class="tree-heading" @click="foldersOpen = !foldersOpen">
        <span class="tree-caret" :class="{ open: foldersOpen }">▶</span>
        分類フォルダ
      </button>
      <ul v-if="foldersOpen" class="tree-list">
        <li v-for="item in folderItems" :key="item" class="tree-item">
          {{ item }}
        </li>
      </ul>
    </section>
  </aside>
</template>

<script setup lang="ts">
defineEmits<{
  'select-preset': [name: string]
}>()

const filterPresetsOpen = ref(true)
const foldersOpen = ref(true)

const presetItems = ['全体', '科', '個人']
const folderItems = ['全体', '科', '個人']
</script>

<style scoped>
.sidebar {
  width: 200px;
  flex-shrink: 0;
  background: var(--color-surface);
  border-right: 1px solid var(--color-border);
  overflow-y: auto;
  padding: 0.5rem 0;
}

.tree-section + .tree-section {
  margin-top: 0.5rem;
}

.tree-heading {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  width: 100%;
  background: none;
  border: none;
  text-align: left;
  padding: 0.4rem 0.9rem;
  font-size: 0.78rem;
  font-weight: 600;
  color: var(--color-text-muted);
  cursor: pointer;
}

.tree-caret {
  display: inline-block;
  font-size: 0.6rem;
  color: var(--color-text-faint);
  transition: transform 0.15s;
}

.tree-caret.open {
  transform: rotate(90deg);
}

.tree-list {
  list-style: none;
  margin: 0;
  padding: 0;
}

.tree-item {
  padding: 0.35rem 0.9rem 0.35rem 2rem;
  font-size: 0.8rem;
  color: var(--color-text);
  cursor: pointer;
  border-left: 2px solid transparent;
}

.tree-item:hover {
  background: var(--color-surface-alt);
}

.tree-item.active {
  color: var(--color-accent);
  border-left-color: var(--color-accent);
  background: var(--color-accent-bg);
}
</style>
