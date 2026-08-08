// ======================================================
// composables/useFilterSort.ts — Notion風のフィルター・ソートの共通ロジック
// ======================================================
// Vue版 frontend/src/composables/useFilterSort.ts の移植（挙動は完全に同一）。
// Notionのデータベースビューにある「+ フィルターを追加」のように、
// 複数のフィルター条件（すべて満たすものだけ表示＝AND）を組み立てられる。
// ソートは「カラム名（列見出し）をクリックすると昇順→降順→ソート解除の順に切り替わる」方式。
// ソートが有効な間は表示順がソート結果で決まるため、ドラッグ&ドロップでの手動並べ替えは
// 意味を持たない（呼び出し側で composables/useDragSort.ts と組み合わせる）。

export type FilterOperator =
  | 'contains' // 含む
  | 'notContains' // 含まない
  | 'equals' // と等しい
  | 'isEmpty' // 空である
  | 'isNotEmpty' // 空でない
  | 'onOrAfter' // 以降（日付用）
  | 'onOrBefore' // 以前（日付用）

const OPERATORS_WITHOUT_VALUE: FilterOperator[] = ['isEmpty', 'isNotEmpty']

export function operatorNeedsValue(operator: FilterOperator): boolean {
  return !OPERATORS_WITHOUT_VALUE.includes(operator)
}

export interface FilterRule<TField extends string> {
  id: string
  field: TField
  operator: FilterOperator
  value: string
}

export type SortDirection = 'asc' | 'desc'

export interface SortState<TField extends string> {
  field: TField
  direction: SortDirection
}

let ruleIdCounter = 0
function nextRuleId(): string {
  ruleIdCounter += 1
  return `rule-${ruleIdCounter}`
}

function matchesRule(rawValue: string, rule: { operator: FilterOperator; value: string }): boolean {
  const value = String(rawValue)
  const target = value.toLowerCase()
  const needle = rule.value.toLowerCase()
  switch (rule.operator) {
    case 'contains':
      return target.includes(needle)
    case 'notContains':
      return !target.includes(needle)
    case 'equals':
      return value === rule.value
    case 'isEmpty':
      return value === ''
    case 'isNotEmpty':
      return value !== ''
    case 'onOrAfter':
      return value !== '' && value >= rule.value
    case 'onOrBefore':
      return value !== '' && value <= rule.value
  }
}

export function useFilterSort<T, TField extends string>(
  items: Ref<T[]>,
  getFieldValue: (item: T, field: TField) => string
) {
  const filterRules = ref<FilterRule<TField>[]>([]) as Ref<FilterRule<TField>[]>
  const sort = ref<SortState<TField> | null>(null) as Ref<SortState<TField> | null>

  // フィルターだけを適用した結果（並べ替えは含まない）。手動ドラッグ並べ替え用の元データ。
  const filteredItems = computed<T[]>(() =>
    items.value.filter((item) =>
      filterRules.value.every((rule) => matchesRule(getFieldValue(item, rule.field), rule))
    )
  )

  // フィルター＋（ソート中であれば）ソートまで適用した結果。
  const filteredSortedItems = computed<T[]>(() => {
    if (!sort.value) return filteredItems.value

    const { field, direction } = sort.value
    return [...filteredItems.value].sort((a, b) => {
      const av = String(getFieldValue(a, field))
      const bv = String(getFieldValue(b, field))
      const cmp = av.localeCompare(bv, 'ja')
      return direction === 'asc' ? cmp : -cmp
    })
  })

  function addFilterRule(field: TField) {
    filterRules.value = [...filterRules.value, { id: nextRuleId(), field, operator: 'contains', value: '' }]
  }

  function removeFilterRule(id: string) {
    filterRules.value = filterRules.value.filter((r) => r.id !== id)
  }

  function clearFilters() {
    filterRules.value = []
  }

  // ── カラム名クリックでのソート切り替え ──
  // 1回目: 昇順 → 2回目（同じカラム）: 降順 → 3回目（同じカラム）: 解除（手動並べ替え順に戻る）
  function toggleSort(field: TField) {
    if (!sort.value || sort.value.field !== field) {
      sort.value = { field, direction: 'asc' }
    } else if (sort.value.direction === 'asc') {
      sort.value = { field, direction: 'desc' }
    } else {
      sort.value = null
    }
  }

  return {
    filterRules,
    sort,
    filteredItems,
    filteredSortedItems,
    addFilterRule,
    removeFilterRule,
    clearFilters,
    toggleSort,
  }
}
