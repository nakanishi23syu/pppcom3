// ======================================================
// composables/useReadingStatus.ts — 検査ごとの読影ステータス管理
// ======================================================
// Vue版 frontend/src/composables/useReadingStatus.ts の移植。
// 実際のPACS製品の検査一覧にある「読影ステータス」列（未記入/記入中/一時保存/最終確定）を
// 再現するための状態管理。レポート機能自体はスコープ外のため、見た目と操作感を再現する
// 目的でブラウザのlocalStorageに保存する簡易実装にしている。
//
// 【NuxtでのSSRとlocalStorageの注意点】
// Nuxtの既定動作（SSR）では、このコンポーザブルの初期化コードはまずNode.jsサーバー側でも
// 1回実行される。サーバー側には window/localStorage が存在しないため、`import.meta.client`
// （クライアント実行時だけtrueになるNuxtの組み込みフラグ）でガードし、サーバーでは
// localStorageへアクセスしない（既定値の空オブジェクトを使う）ようにする。
// Vue版（Viteのみ、SSRなし）には無かった配慮がNuxtでは必要になる好例。

export type ReadingStatus = 'not-entered' | 'in-progress' | 'draft-saved' | 'finalized'

export const READING_STATUS_LABELS: Record<ReadingStatus, string> = {
  'not-entered': '未記入',
  'in-progress': '記入中',
  'draft-saved': '一時保存',
  finalized: '最終確定',
}

export const READING_STATUS_OPTIONS: ReadingStatus[] = [
  'not-entered',
  'in-progress',
  'draft-saved',
  'finalized',
]

const STORAGE_KEY = 'dicom-tool.readingStatus'

function loadFromStorage(): Record<string, ReadingStatus> {
  // サーバーサイド（SSR）ではlocalStorageが存在しないため、ここで必ずガードする。
  if (!import.meta.client) return {}
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    return raw ? JSON.parse(raw) : {}
  } catch {
    return {}
  }
}

// モジュールスコープの reactive オブジェクト1つを全コンポーネントで共有する。
const statusMap = reactive<Record<string, ReadingStatus>>(loadFromStorage())

if (import.meta.client) {
  watch(
    statusMap,
    (value) => {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(value))
    },
    { deep: true }
  )
}

export function useReadingStatus() {
  function getStatus(studyInstanceUID: string): ReadingStatus {
    return statusMap[studyInstanceUID] ?? 'not-entered'
  }

  function setStatus(studyInstanceUID: string, status: ReadingStatus) {
    statusMap[studyInstanceUID] = status
  }

  return { statusMap, getStatus, setStatus }
}
