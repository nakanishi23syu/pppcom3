// ======================================================
// types/dicom.ts — DICOM データの型定義
// ======================================================
// Vue版 frontend/src/types/dicom.ts の移植。
// Nuxtでも「型だけを定義するファイル」の置き場所は自由だが、他のfeatureからも
// 参照しやすいよう app/types/ に置いている（tsconfig.jsonの `~/*` エイリアスで参照可能）。
//
// DICOM の階層構造:
//   Study（検査） > Series（シリーズ） > Instance（画像1枚）
//   例: CT検査 > 腹部軸位断 > 200枚のスライス

// ── 検査（Study）──────────────────────────────────────
export interface DicomStudy {
  studyInstanceUID: string // 検査を一意に識別するID（世界中で重複しない）
  patientName: string // 患者名（DICOM タグ: 0010,0010）
  patientID: string // 患者ID（DICOM タグ: 0010,0020）
  studyDate: string // 検査日（YYYYMMDD形式。例: "20240315"）
  studyDescription: string // 検査説明（例: "腹部CT"）
  modality: string // モダリティ（撮影装置の種類。例: CT, MR, CR）
  accessionNumber: string // アクセッション番号（RIS/HIS での管理番号）
  series: DicomSeries[] // この検査に含まれるシリーズの配列
  filePath: string // 代表ファイルのパス（検査レベルのサムネイル用）
  order: number // Notion風ドラッグ&ドロップ並べ替えの表示順（backendのUserStudy.Orderに対応）
}

// ── シリーズ（Series）─────────────────────────────────
export interface DicomSeries {
  seriesInstanceUID: string
  seriesNumber: string
  seriesDescription: string
  modality: string
  numberOfInstances: number
  instances: DicomInstance[]
  order: number
}

// ── インスタンス（Instance）───────────────────────────
export interface DicomInstance {
  sopInstanceUID: string
  instanceNumber: string
  filePath: string
  order: number
}
