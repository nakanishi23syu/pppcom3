// ======================================================
// composables/useToast.ts — 汎用トースト通知（成功・エラー・情報表示の共通化）
// ======================================================
// Vue版 frontend/src/composables/useToast.ts の移植。
// composables/ ディレクトリに置くと、Nuxtのauto-importにより
// `import { useToast } from '...'` を書かずにコンポーネントからそのまま呼べる
// （AutoImportUnit.vue で詳しく解説する）。
//
// Pinia化するほどの複雑な状態ではないため、モジュールスコープの ref な配列を
// 「唯一のインスタンス」として全コンポーネントで共有するシンプルな実装にしている。
//
// 【使い方】
//   const toast = useToast()
//   toast.success('保存しました')
//   toast.error(e instanceof Error ? e.message : '保存に失敗しました')

export type ToastType = 'success' | 'error' | 'info' | 'loading'

export interface Toast {
  id: number
  type: ToastType
  message: string
  duration: number
}

// モジュールスコープに置くことで、どのコンポーネントから useToast() を呼んでも
// 同じ配列を共有する（＝どこで表示してもToastContainer.vue 1箇所に反映される）。
const toasts = ref<Toast[]>([])
let nextId = 1

const DEFAULT_DURATION: Record<ToastType, number> = {
  success: 3000,
  error: 5000,
  info: 3000,
  loading: 0,
}

function dismiss(id: number) {
  toasts.value = toasts.value.filter((t) => t.id !== id)
}

function push(type: ToastType, message: string, duration?: number): number {
  const id = nextId++
  const resolvedDuration = duration ?? DEFAULT_DURATION[type]
  toasts.value.push({ id, type, message, duration: resolvedDuration })
  if (resolvedDuration > 0) {
    setTimeout(() => dismiss(id), resolvedDuration)
  }
  return id
}

function update(id: number, type: ToastType, message: string, duration?: number) {
  const toast = toasts.value.find((t) => t.id === id)
  if (!toast) return
  toast.type = type
  toast.message = message
  const resolvedDuration = duration ?? DEFAULT_DURATION[type]
  toast.duration = resolvedDuration
  if (resolvedDuration > 0) {
    setTimeout(() => dismiss(id), resolvedDuration)
  }
}

export function useToast() {
  return {
    toasts,
    dismiss,
    success: (message: string, duration?: number) => push('success', message, duration),
    error: (message: string, duration?: number) => push('error', message, duration),
    info: (message: string, duration?: number) => push('info', message, duration),
    loading: (message: string) => push('loading', message, 0),
    update,
  }
}
