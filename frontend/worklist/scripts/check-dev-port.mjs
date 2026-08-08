// ======================================================
// scripts/check-dev-port.mjs — 開発サーバー起動前のポート占有チェック
// ======================================================
// 移植元(dicom-tool-2/nuxt)と同じ対策。Nuxtの開発サーバーはポートが埋まっていても
// 黙って別の空きポートにフォールバックしてしまうため、Backend APIのCORS許可オリジンが
// "http://localhost:3100" 固定（docs/CONTRACT.md参照）である以上、意図しないポートに
// フォールバックすると原因の分かりにくい「Failed to fetch」になる。事前に検知して止める。
import net from 'node:net'

const PORT = 3100

function isPortFree(port) {
  return new Promise((resolve) => {
    const tester = net.createServer()
    tester.once('error', () => resolve(false))
    tester.once('listening', () => {
      tester.close(() => resolve(true))
    })
    tester.listen(port, '0.0.0.0')
  })
}

const free = await isPortFree(PORT)

if (!free) {
  console.error(
    `\n[check-dev-port] ポート${PORT}は既に別のプロセスが使用しています。\n` +
      `Nuxtはこの状態で起動すると、確認なしに別のポートへ自動的に切り替わってしまい、\n` +
      `Backend API側のCORS許可オリジン（http://localhost:${PORT}固定）と一致しなくなって\n` +
      `「Failed to fetch」という分かりにくいエラーになります。\n\n` +
      `PowerShellで以下を実行し、${PORT}番を掴んでいるプロセスを停止してから再実行してください:\n` +
      `  Get-NetTCPConnection -LocalPort ${PORT} | Select-Object OwningProcess\n` +
      `  Stop-Process -Id <上記のPID> -Force\n`
  )
  process.exit(1)
}
