// ======================================================
// utils/dicomService.ts — DICOM データアクセス層
// ======================================================
// Vue版 frontend/src/services/dicomService.ts の移植。
// dicom.ts というnpmライブラリでバイナリをパースし、GraphQLのレスポンス（backendApiService.ts）を
// 画面用の型（types/dicom.ts）に変換する。Vue（リアクティビティ）に一切依存しない純粋な層。

import type { GraphQLStudy, GraphQLSeries, GraphQLSop } from './backendApiService'
import type { DicomStudy, DicomSeries, DicomInstance } from '~/types/dicom'

// ======================================================
// parseDicomFile — .dcm ファイルをパースして DCMImage を返す
// ======================================================
// 【NuxtのSSRとdicom.tsライブラリの相性について（重要）】
// dicom.ts はcanvasへのWebGL描画を前提としたブラウザ専用ライブラリで、内部で使っている
// @wearemothership/dicom-character-set というCommonJSパッケージが、Node.js（SSR実行環境）
// 上でのESM名前付きimportに対応していない。ファイル冒頭で `import dicomts from 'dicom.ts'`
// のように静的importすると、このファイル（dicomService.ts）を参照しているだけで
// Nuxtのサーバーサイドバンドルにもdicom.tsが巻き込まれてしまい、SSR時にエラーで落ちる。
// 対策として、dicom.tsは「実際に呼び出される瞬間（＝必ずブラウザ上）」に動的import
// （`await import('dicom.ts')`）する。動的importはその行が実行されるまでモジュールを
// 読み込まないため、静的解析でSSRバンドルに含まれることもなくなる。
// なお parseDicomFile/renderDicomToCanvas はどちらもcanvas要素（DOM）を扱う都合上、
// 呼び出し元（SeriesThumbnailPanel.vue等）は必ずonMounted/watch等のクライアント実行
// タイミングでしか呼ばないため、import.meta.clientによる追加ガードは行っていない。
export async function parseDicomFile(filePath: string) {
  const dicomts = (await import('dicom.ts')).default
  const res = await fetch(filePath)
  if (!res.ok) throw new Error(`ファイルの取得に失敗しました: ${filePath}`)
  const buffer = await res.arrayBuffer()
  return dicomts.parseImage(buffer) // パース失敗時は null を返す
}

// ======================================================
// renderDicomToCanvas — .dcm ファイルを <canvas> に描画する
// ======================================================
export async function renderDicomToCanvas(
  filePath: string,
  canvas: HTMLCanvasElement,
  scale = 1
): Promise<void> {
  const dicomts = (await import('dicom.ts')).default
  const image = await parseDicomFile(filePath)
  if (!image) throw new Error('DICOMファイルのパースに失敗しました')
  await dicomts.render(image, canvas, scale)
}

// ======================================================
// getDicomFileBaseUrl — DICOMファイル配信の静的URLのベースを組み立てる
// ======================================================
// backendのGraphQLエンドポイント（例: http://localhost:5030/graphql）から
// `/graphql` を取り除き、静的配信パス `/dicom-files` を付け直す。
function getDicomFileBaseUrl(): string {
  const config = useRuntimeConfig()
  return config.public.graphqlEndpoint.replace(/\/graphql$/, '/dicom-files')
}

// ======================================================
// mapBackendStudy / mapBackendSeries / mapBackendSop
// ======================================================
// backendApiService.ts が返すGraphQLの型（フィールド名がcamelCaseのUid等）を、
// 画面側コンポーネントが期待する types/dicom.ts の型（DICOM由来の慣習でUID等）に変換する。
export function mapBackendStudy(study: GraphQLStudy): DicomStudy {
  const series = study.series.map(mapBackendSeries)
  return {
    studyInstanceUID: study.studyInstanceUid,
    patientName: study.patientName,
    patientID: study.patientId,
    // GraphQLのDateスカラーは "yyyy-MM-dd" で返るため、既存コード（formatDate等）が
    // 前提とする "yyyyMMdd" の8文字形式に合わせてハイフンを取り除く。
    studyDate: study.studyDate.split('-').join(''),
    studyDescription: study.studyDescription,
    modality: study.modality,
    accessionNumber: study.accessionNumber,
    series,
    filePath: series[0]?.instances[0]?.filePath ?? '',
    order: study.order,
  }
}

function mapBackendSeries(series: GraphQLSeries): DicomSeries {
  const instances = series.sops.map(mapBackendSop)
  return {
    seriesInstanceUID: series.seriesInstanceUid,
    seriesNumber: series.seriesNumber,
    seriesDescription: series.seriesDescription,
    modality: series.modality,
    numberOfInstances: instances.length,
    instances,
    order: series.order,
  }
}

function mapBackendSop(sop: GraphQLSop): DicomInstance {
  return {
    sopInstanceUID: sop.sopInstanceUid,
    instanceNumber: sop.instanceNumber,
    filePath: `${getDicomFileBaseUrl()}/${sop.filePath}`,
    order: sop.order,
  }
}
