// ======================================================
// utils/uploadService.ts — backend へのDICOMファイルアップロード
// ======================================================
// Vue版 frontend/src/services/uploadService.ts は「素のmultipart/form-data POST」を
// REST的な `/api/dicom-upload` エンドポイントへ送る実装だったが、backend側はその後
// `uploadDicomFiles(files: [Upload!]!): [DicomUploadResult!]!` というGraphQL Mutationに
// 統合された。GraphQLは本来JSONしかやり取りできないため、ファイル（バイナリ）を
// Mutationの引数として送るには "graphql-multipart-request-spec" という業界標準の
// 拡張仕様に従ってmultipart/form-dataを手組みする必要がある。
// 仕様: https://github.com/jaydenseric/graphql-multipart-request-spec
//
// 【multipart request specの3パート構成】
//   1. "operations" パート: 通常のGraphQLリクエスト本体（query + variables）をJSON文字列で入れる。
//      ただしファイルを入れるべき場所には代わりに null を置く（後で"map"が実際の値に差し替える）。
//   2. "map" パート: 「multipartの何番目のパートが、operationsのどこ（JSON Pointer的なパス）に
//      対応するか」を表すJSON。例: {"0": ["variables.files.0"], "1": ["variables.files.1"]}
//   3. ファイル本体のパート: パート名を"map"のキー（"0", "1", ...）に一致させて、
//      Fileオブジェクトをそのままappendする。
//
// HotChocolate（backend側のGraphQLサーバー）はこの仕様に標準対応しているため、
// 上記の形式でPOSTするだけで `files: [Upload!]!` 引数にファイル配列として渡る。

export interface DicomUploadResult {
  fileName: string
  workflowId: string
  accepted: boolean
  message: string
}

interface UploadDicomFilesResponse {
  uploadDicomFiles: DicomUploadResult[]
}

// ======================================================
// uploadDicomFiles — 複数ファイルを1回のリクエストでまとめて送る
// ======================================================
export async function uploadDicomFiles(files: File[]): Promise<DicomUploadResult[]> {
  const config = useRuntimeConfig()
  const endpoint = config.public.graphqlEndpoint

  // ① GraphQL Mutation本体。files変数の実際の値はここではnullの配列にしておき、
  //    multipart仕様に従って後からブラウザ側でファイル本体に「つなぎ直して」もらう。
  const query = `
    mutation UploadDicomFiles($files: [Upload!]!) {
      uploadDicomFiles(files: $files) {
        fileName
        workflowId
        accepted
        message
      }
    }
  `
  const operations = {
    query,
    variables: {
      files: files.map(() => null),
    },
  }

  // ② map: パート名("0","1",...) → operations内でファイルが入るべき場所(JSON Pointer風の配列)
  const map: Record<string, string[]> = {}
  files.forEach((_, index) => {
    map[String(index)] = [`variables.files.${index}`]
  })

  // ③ multipart/form-dataを実際に組み立てる。
  const formData = new FormData()
  formData.append('operations', JSON.stringify(operations))
  formData.append('map', JSON.stringify(map))
  files.forEach((file, index) => {
    formData.append(String(index), file)
  })

  const res = await fetch(endpoint, {
    method: 'POST',
    credentials: 'include', // アップロードもログインユーザー情報を必要とする可能性があるため送信する
    headers: {
      // HotChocolate（backendのGraphQLサーバー）はマルチパートのファイルアップロードに対して、
      // CSRF対策のプリフライト保護を既定で有効にしている（HC0077エラー）。
      // 普通の<form>タグによる multipart/form-data 送信は「シンプルリクエスト」として
      // ブラウザのCORSプリフライト（事前のOPTIONS確認）をスキップしてしまうため、
      // 悪意あるサイトに置かれた隠しフォームから知らないうちにファイルをアップロード
      // させられてしまう（クロスサイトリクエストフォージェリ）リスクがある。
      // このヘッダーを1つ付けるだけで「シンプルリクエストではなくなり」、ブラウザが
      // 必ずCORSプリフライトを行うようになるため、他サイトからの無断アップロードを防げる。
      'GraphQL-preflight': '1',
    },
    body: formData,
    // Content-Type は指定しない。fetchがFormDataを渡されると、境界文字列(boundary)を含む
    // 正しい `multipart/form-data; boundary=...` を自動設定してくれる。
    // 手動でヘッダーを付けるとboundaryが欠落してbackend側でパースに失敗する。
  })

  if (!res.ok) {
    throw new Error(`アップロードに失敗しました (HTTP ${res.status})`)
  }

  const json: { data?: UploadDicomFilesResponse; errors?: { message: string }[] } =
    await res.json()

  if (json.errors && json.errors.length > 0) {
    throw new Error(json.errors.map((e) => e.message).join(', '))
  }
  if (!json.data) {
    throw new Error('アップロードのレスポンスにdataが含まれていません')
  }

  return json.data.uploadDicomFiles
}
