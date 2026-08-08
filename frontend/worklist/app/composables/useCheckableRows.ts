// ======================================================
// composables/useCheckableRows.ts — Notion風チェックボックス選択の共通ロジック
// ======================================================
// Vue版 frontend/src/composables/useCheckableRows.ts の移植。
// 「どの行がチェックされているか」だけを持つシンプルなComposable。
// チェックされた行は呼び出し側（StudyTable.vue等）で
//   - 編集可能なセル（<input>）に切り替える
//   - 1件以上チェックされていればツールバーに削除ボタンを表示する
// という2つの用途に使う。

export function useCheckableRows<T>(getId: (item: T) => string) {
  const checkedIds = ref<Set<string>>(new Set())

  function isChecked(item: T): boolean {
    return checkedIds.value.has(getId(item))
  }

  function toggle(item: T) {
    const id = getId(item)
    const next = new Set(checkedIds.value)
    if (next.has(id)) {
      next.delete(id)
    } else {
      next.add(id)
    }
    checkedIds.value = next
  }

  function clear() {
    checkedIds.value = new Set()
  }

  return { checkedIds, isChecked, toggle, clear }
}
