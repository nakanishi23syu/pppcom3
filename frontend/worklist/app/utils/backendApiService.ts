// ======================================================
// utils/backendApiService.ts — backend/DicomLearning.GraphQL とのやり取り
// ======================================================
// Vue版 frontend/src/services/backendApiService.ts の移植。
// dicomService.ts が dicom.ts というライブラリとの通信を担当する層であるのと同じように、
// このファイルは「backendのGraphQL APIとの通信」を担当する層、という位置づけになる。
//
// graphqlRequest() は app/utils/graphqlClient.ts で定義されており、Nuxtのauto-importにより
// ここでは import 文を書かずにそのまま呼べる（GraphQLUnit.vue で詳しく解説する）。

export interface GraphQLSop {
  sopInstanceUid: string
  instanceNumber: string
  filePath: string
  isRead: boolean
  readAt: string | null
  readByUserId: string | null
  order: number
}

export interface GraphQLSeries {
  seriesInstanceUid: string
  seriesNumber: string
  seriesDescription: string
  modality: string
  order: number
  sops: GraphQLSop[]
}

export interface GraphQLStudy {
  studyInstanceUid: string
  patientName: string
  patientId: string
  studyDate: string
  studyDescription: string
  modality: string
  accessionNumber: string
  bodyPartExamined: string
  order: number
  series: GraphQLSeries[]
}

// ======================================================
// fetchUnreadInstances — 未読の画像一覧を取得する（Query の呼び方の例）
// ======================================================
export async function fetchUnreadInstances(): Promise<GraphQLSop[]> {
  const query = `
    query UnreadInstances {
      unreadInstances {
        sopInstanceUid
        instanceNumber
        filePath
        isRead
        readAt
        readByUserId
        order
      }
    }
  `
  const data = await graphqlRequest<{ unreadInstances: GraphQLSop[] }>(query)
  return data.unreadInstances
}

// ======================================================
// markInstanceAsRead / markInstanceAsUnread — 画像の既読/未読切り替え（Mutation の例）
// ======================================================
export async function markInstanceAsRead(
  sopInstanceUid: string,
  userId: string
): Promise<GraphQLSop> {
  const query = `
    mutation MarkInstanceAsRead($sopInstanceUid: String!, $userId: String!) {
      markInstanceAsRead(sopInstanceUid: $sopInstanceUid, userId: $userId) {
        sopInstanceUid
        isRead
        readAt
        readByUserId
      }
    }
  `
  const data = await graphqlRequest<{ markInstanceAsRead: GraphQLSop }>(query, {
    sopInstanceUid,
    userId,
  })
  return data.markInstanceAsRead
}

export async function markInstanceAsUnread(sopInstanceUid: string): Promise<GraphQLSop> {
  const query = `
    mutation MarkInstanceAsUnread($sopInstanceUid: String!) {
      markInstanceAsUnread(sopInstanceUid: $sopInstanceUid) {
        sopInstanceUid
        isRead
      }
    }
  `
  const data = await graphqlRequest<{ markInstanceAsUnread: GraphQLSop }>(query, {
    sopInstanceUid,
  })
  return data.markInstanceAsUnread
}

// ======================================================
// fetchStudies — 検査一覧（シリーズ・SOPまで含む階層）を取得する
// ======================================================
export async function fetchStudies(): Promise<GraphQLStudy[]> {
  const query = `
    query Studies {
      studies {
        studyInstanceUid
        patientName
        patientId
        studyDate
        studyDescription
        modality
        accessionNumber
        bodyPartExamined
        order
        series {
          seriesInstanceUid
          seriesNumber
          seriesDescription
          modality
          order
          sops {
            sopInstanceUid
            instanceNumber
            filePath
            isRead
            readAt
            readByUserId
            order
          }
        }
      }
    }
  `
  const data = await graphqlRequest<{ studies: GraphQLStudy[] }>(query)
  return data.studies
}

// ======================================================
// fetchPatientTimeline — 患者ごとの検査履歴を新しい順に取得する
// ======================================================
export async function fetchPatientTimeline(patientId: string): Promise<GraphQLStudy[]> {
  const query = `
    query PatientTimeline($patientId: String!) {
      patientTimeline(patientId: $patientId) {
        studyInstanceUid
        patientName
        patientId
        studyDate
        studyDescription
        modality
        accessionNumber
        bodyPartExamined
        order
        series {
          seriesInstanceUid
          seriesNumber
          seriesDescription
          modality
          order
          sops {
            sopInstanceUid
            instanceNumber
            filePath
            isRead
            readAt
            readByUserId
            order
          }
        }
      }
    }
  `
  const data = await graphqlRequest<{ patientTimeline: GraphQLStudy[] }>(query, { patientId })
  return data.patientTimeline
}

// ======================================================
// 変更の保存（並べ替え + インライン編集の統合Mutation）
// ======================================================
// composables/useEditableList.ts が元データとの差分（変更された行だけ）を検出し、
// ここでは1つの保存ボタンからその差分をまとめて送るだけにする。
// 各フィールドはundefinedなら「変更しない」扱い。orderが含まれる変更は管理者のみ許可される。
export interface StudyChangeInput {
  studyInstanceUid: string
  order?: number
  patientId?: string
  patientName?: string
  studyDate?: string // "YYYY-MM-DD"形式
  studyDescription?: string
  modality?: string
}

export async function saveStudyChanges(changes: StudyChangeInput[]): Promise<number> {
  const query = `
    mutation SaveStudyChanges($changes: [StudyChangeInput!]!) {
      saveStudyChanges(changes: $changes)
    }
  `
  const data = await graphqlRequest<{ saveStudyChanges: number }>(query, { changes })
  return data.saveStudyChanges
}

export interface SeriesChangeInput {
  seriesInstanceUid: string
  order?: number
  seriesNumber?: string
  seriesDescription?: string
  modality?: string
}

export async function saveSeriesChanges(changes: SeriesChangeInput[]): Promise<number> {
  const query = `
    mutation SaveSeriesChanges($changes: [SeriesChangeInput!]!) {
      saveSeriesChanges(changes: $changes)
    }
  `
  const data = await graphqlRequest<{ saveSeriesChanges: number }>(query, { changes })
  return data.saveSeriesChanges
}

export interface SopChangeInput {
  sopInstanceUid: string
  order?: number
  instanceNumber?: string
}

export async function saveSopChanges(changes: SopChangeInput[]): Promise<number> {
  const query = `
    mutation SaveSopChanges($changes: [SopChangeInput!]!) {
      saveSopChanges(changes: $changes)
    }
  `
  const data = await graphqlRequest<{ saveSopChanges: number }>(query, { changes })
  return data.saveSopChanges
}

// ======================================================
// DICOMタグへの復元（インライン編集で上書きした値を元のタグ値に戻す）
// ======================================================
export interface RevertedStudyFields {
  studyInstanceUid: string
  patientId: string
  patientName: string
  studyDate: string
  studyDescription: string
  modality: string
  accessionNumber: string
  bodyPartExamined: string
}

export async function revertStudyFields(studyInstanceUid: string): Promise<RevertedStudyFields> {
  const query = `
    mutation RevertStudyFields($studyInstanceUid: String!) {
      revertStudyFields(studyInstanceUid: $studyInstanceUid) {
        studyInstanceUid
        patientId
        patientName
        studyDate
        studyDescription
        modality
        accessionNumber
        bodyPartExamined
      }
    }
  `
  const data = await graphqlRequest<{ revertStudyFields: RevertedStudyFields }>(query, {
    studyInstanceUid,
  })
  return data.revertStudyFields
}

export interface RevertedSeriesFields {
  seriesInstanceUid: string
  seriesNumber: string
  seriesDescription: string
  modality: string
}

export async function revertSeriesFields(seriesInstanceUid: string): Promise<RevertedSeriesFields> {
  const query = `
    mutation RevertSeriesFields($seriesInstanceUid: String!) {
      revertSeriesFields(seriesInstanceUid: $seriesInstanceUid) {
        seriesInstanceUid
        seriesNumber
        seriesDescription
        modality
      }
    }
  `
  const data = await graphqlRequest<{ revertSeriesFields: RevertedSeriesFields }>(query, {
    seriesInstanceUid,
  })
  return data.revertSeriesFields
}

export interface RevertedSopFields {
  sopInstanceUid: string
  instanceNumber: string
}

export async function revertSopFields(sopInstanceUid: string): Promise<RevertedSopFields> {
  const query = `
    mutation RevertSopFields($sopInstanceUid: String!) {
      revertSopFields(sopInstanceUid: $sopInstanceUid) {
        sopInstanceUid
        instanceNumber
      }
    }
  `
  const data = await graphqlRequest<{ revertSopFields: RevertedSopFields }>(query, {
    sopInstanceUid,
  })
  return data.revertSopFields
}

// ======================================================
// カスケード削除（DBのレコード＋紐づくDICOMファイルを削除する）
// ======================================================
export async function deleteStudy(studyInstanceUid: string): Promise<void> {
  const query = `
    mutation DeleteStudy($studyInstanceUid: String!) {
      deleteStudy(studyInstanceUid: $studyInstanceUid)
    }
  `
  await graphqlRequest<{ deleteStudy: boolean }>(query, { studyInstanceUid })
}

export async function deleteSeries(seriesInstanceUid: string): Promise<void> {
  const query = `
    mutation DeleteSeries($seriesInstanceUid: String!) {
      deleteSeries(seriesInstanceUid: $seriesInstanceUid)
    }
  `
  await graphqlRequest<{ deleteSeries: boolean }>(query, { seriesInstanceUid })
}

export async function deleteSop(sopInstanceUid: string): Promise<void> {
  const query = `
    mutation DeleteSop($sopInstanceUid: String!) {
      deleteSop(sopInstanceUid: $sopInstanceUid)
    }
  `
  await graphqlRequest<{ deleteSop: boolean }>(query, { sopInstanceUid })
}
