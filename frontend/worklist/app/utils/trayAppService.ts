// ======================================================
// utils/trayAppService.ts — 常駐トレイアプリ(TrayApp)のローカルHTTP APIを呼び出す
// ======================================================
// 【なぜTrayApp経由でTimelineを開くのか（サービス間導線の設計メモ）】
// Worklist(このアプリ、Nuxt/3100)自身はTimeline機能(Blazor WebAssembly/5230)の実装を
// 持たない（別サービスとして分離されているため。docs/CONTRACT.md参照）。
// ブラウザから直接 `window.open('http://localhost:5230/...')` することも技術的には
// 可能だが、このプロジェクトでは学習目的であえて「常駐デスクトップアプリ経由で
// ブラウザを起動させる」という、実務のPACS/レポーティングシステム連携（外部ビューアを
// キック起動する等）に近い構図を再現している。Worklistはローカルの常駐アプリ
// （services/DicomTool.TrayApp、ポート5299）にHTTP POSTで「開いてほしい」という命令だけを
// 送り、実際にブラウザタブを開く操作（Process.Startによるシェル起動）はTrayApp側に委ねる。
//
// 【接続失敗時の扱い】
// TrayAppはこのWorklistとは別プロセスのため、常駐アプリが起動していない場合は
// fetch自体がネットワークエラーで失敗する（ブラウザは「CORSエラー」または
// 「ERR_CONNECTION_REFUSED」的な例外としてfetchのPromiseをrejectする）。
// これを呼び出し側（StudyTable.vue）で捕まえやすいよう、専用のエラー型に変換して投げる。

// TrayAppへの接続自体が失敗した（＝常駐アプリが起動していない可能性が高い）ことを表すエラー。
export class TrayAppUnreachableError extends Error {
  constructor(
    message = '常駐アプリ（TrayApp）が起動していません。DicomTool.TrayApp を起動してから、もう一度お試しください。'
  ) {
    super(message)
    this.name = 'TrayAppUnreachableError'
  }
}

function getTrayAppEndpoint(): string {
  const config = useRuntimeConfig()
  return config.public.trayAppEndpoint
}

// ======================================================
// openTimeline — TrayAppに「この患者のTimelineをブラウザで開いて」と命令する
// ======================================================
// services/DicomTool.TrayApp/Program.cs の `POST /commands/open-timeline` に対応する。
// リクエストボディは同サービスの Models/OpenTimelineRequest.cs（record OpenTimelineRequest(string? PatientId)）
// に合わせ、ASP.NET Coreの既定シリアライズ規則（camelCase）に沿って { patientId } で送る。
export async function openTimeline(patientId: string): Promise<void> {
  const endpoint = getTrayAppEndpoint()

  let res: Response
  try {
    res = await fetch(`${endpoint}/commands/open-timeline`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ patientId }),
    })
  } catch {
    // fetch自体の例外（ネットワークエラー）は、TrayAppが起動していない場合の典型的な挙動。
    // HTTPレスポンスとしてのエラー（4xx/5xx）とは別に、ここで専用の型に変換しておく。
    throw new TrayAppUnreachableError()
  }

  if (!res.ok) {
    let detail = ''
    try {
      const body = (await res.json()) as { error?: string; detail?: string }
      detail = body.error ?? body.detail ?? ''
    } catch {
      // レスポンスボディがJSONでない場合は無視し、HTTPステータスだけを伝える。
    }
    throw new Error(`常駐アプリへの命令に失敗しました (HTTP ${res.status})${detail ? `: ${detail}` : ''}`)
  }
}
