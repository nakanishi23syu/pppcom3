// ======================================================
// composables/useEditableList.ts — 並べ替え + インライン編集の統合保存（共通ロジック）
// ======================================================
// Vue版 frontend/src/composables/useEditableList.ts の移植（挙動は完全に同一）。
// 「元データ（items）を最初にスナップショットし、ドラッグ並べ替え・v-modelでのセル編集は
// ローカルの作業用配列（workingItems）だけを書き換え、1つの保存ボタンでスナップショットとの
// 差分だけをbackendへ送る」方式に統一する。検査・シリーズ・SOPの3箇所で共通して使う。

export interface EditableListOptions<T> {
  getId: (item: T) => string
  getOrder: (item: T) => number
  getFields: (item: T) => Record<string, string>
}

interface Snapshot {
  order: number
  fields: Record<string, string>
}

export interface ItemPatch {
  order?: number
  fields?: Record<string, string>
}

export function useEditableList<T>(items: Ref<T[]>, options: EditableListOptions<T>) {
  const { getId, getOrder, getFields } = options

  const workingItems = ref<T[]>([]) as Ref<T[]>
  let snapshot = new Map<string, Snapshot>()

  const dirty = ref(false)
  const saving = ref(false)
  const saveError = ref<string | null>(null)

  // apply()自身によるworkingItemsへの代入を、下のwatchがdirty化と誤認しないようにするフラグ。
  let suppressDirty = false

  function takeSnapshot(list: T[]): Map<string, Snapshot> {
    const map = new Map<string, Snapshot>()
    list.forEach((item, index) => {
      map.set(getId(item), { order: index, fields: getFields(item) })
    })
    return map
  }

  // items（元データ）をDBのorder昇順に並べ替えて作業用配列にセットし直し、
  // その状態を新しいスナップショット（＝以後の差分検出の基準）として確定する。
  function apply() {
    suppressDirty = true
    workingItems.value = [...items.value].sort((a, b) => getOrder(a) - getOrder(b))
    snapshot = takeSnapshot(workingItems.value)
    dirty.value = false
  }

  // ドラッグ並べ替え（配列の入れ替え）・v-modelでのセル編集（プロパティ変更）の両方を
  // 1つのdirty判定で拾うため、配列参照だけでなくプロパティの変更もdeepで監視する。
  //
  // 【注意: このwatchは下の「items」watchより先に登録する必要がある】
  // 下の items watch は immediate: true のため、watch() を呼び出した瞬間に同期的に
  // コールバックが実行され、その中で apply() が workingItems.value を書き換える。
  // もしこの workingItems watch が「まだ登録されていない」タイミングで apply() が
  // 呼ばれると、suppressDirty フラグを true にした直後にそれを消費するリスナーが
  // 存在しないため、フラグが true のまま取り残されてしまう。その後ユーザーが最初に
  // 行うドラッグ並べ替え・インライン編集（＝workingItemsへの最初の実変更）が、
  // 「apply()由来の変更」と誤認されてdirty化がスキップされ、
  // 保存ボタンがずっと無効のまま反映されないという不具合が実機テストで見つかった
  // （StudyTable.vue・SeriesListPanel.vue・SopListPanel.vueいずれも影響。
  // StudyTableだけ症状が出にくかったのは、選択直後にfetchStudies()の完了で
  // studies配列が再代入され、そのタイミングでこのwatchが既に存在していたため
  // 偶然フラグが正しく消費されていたから）。
  // 対策として、このwatchを先に登録し、apply()による書き換えを必ずこのwatchが
  // 観測できる状態にしてからitems watchのimmediateコールバックを走らせる。
  watch(
    workingItems,
    () => {
      if (suppressDirty) {
        suppressDirty = false
        return
      }
      dirty.value = true
    },
    { deep: true }
  )

  // items（backendからの再取得結果等）が変わるたびに、まだ編集していないなら追随する。
  watch(
    items,
    () => {
      if (!dirty.value) apply()
    },
    { immediate: true }
  )

  // 現在のworkingItemsをスナップショットと突き合わせ、実際に変更された行だけを抽出する。
  function collectChanges<C>(toChange: (item: T, patch: ItemPatch) => C): C[] {
    const changes: C[] = []
    workingItems.value.forEach((item, index) => {
      const snap = snapshot.get(getId(item))
      const currentFields = getFields(item)
      const orderChanged = !snap || snap.order !== index

      const changedFields: Record<string, string> = {}
      for (const key of Object.keys(currentFields)) {
        // key は Object.keys(currentFields) から得ているため必ず存在するが、
        // noUncheckedIndexedAccess（tsconfig参照）により添字アクセスの型は
        // 「無いかもしれない」を含む string | undefined になる。存在確定なので明示的に確定させる。
        const value = currentFields[key] as string
        if (!snap || value !== snap.fields[key]) {
          changedFields[key] = value
        }
      }
      const hasFieldChanges = Object.keys(changedFields).length > 0

      if (orderChanged || hasFieldChanges) {
        changes.push(
          toChange(item, {
            order: orderChanged ? index : undefined,
            fields: hasFieldChanges ? changedFields : undefined,
          })
        )
      }
    })
    return changes
  }

  // 差分だけをmutationFnに渡して保存する。変更が無ければ何もしない。
  async function save<C>(
    toChange: (item: T, patch: ItemPatch) => C,
    mutationFn: (changes: C[]) => Promise<number>
  ): Promise<number> {
    const changes = collectChanges(toChange)
    if (changes.length === 0) return 0

    saving.value = true
    saveError.value = null
    try {
      const count = await mutationFn(changes)
      snapshot = takeSnapshot(workingItems.value)
      dirty.value = false
      return count
    } catch (e) {
      saveError.value = e instanceof Error ? e.message : '保存に失敗しました'
      throw e
    } finally {
      saving.value = false
    }
  }

  return { workingItems, dirty, saving, saveError, apply, save }
}
