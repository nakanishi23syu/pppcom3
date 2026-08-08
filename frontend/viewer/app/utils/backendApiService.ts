// ======================================================
// utils/backendApiService.ts — Backend API(GraphQL)とのやり取り
// ======================================================
// Worklistアプリの同名ファイル(frontend/worklist/app/utils/backendApiService.ts)からの
// 移植だが、Viewerは画像表示に特化しているため、検査一覧の取得(fetchStudies)に必要な部分
// だけを残し、並べ替え保存・DICOMタグへの復元・削除など編集系のMutationは含めていない
// （それらはWorklist側の責務。コード重複を許容しつつ、各サービスが実際に必要とする範囲
// だけを持つ、という方針は指示書の通り）。
//
// graphqlRequest() は app/utils/graphqlClient.ts で定義されており、Nuxtのauto-importにより
// ここでは import 文を書かずにそのまま呼べる。

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
// fetchStudies — 検査一覧（シリーズ・SOPまで含む階層）を取得する
// ======================================================
// Viewerはこの画面単体では「どのシリーズを表示するか」をURLパラメータ(seriesInstanceUID)
// からしか知らない（stores/dicomStore.ts参照）。全検査を取得したうえで該当シリーズを
// 探し出す実装は冗長に見えるが、直接URLアクセス（リロード・ブックマーク・Timelineからの
// 起動等）された場合でも同じロジック1本で対応できるよう、あえてWorklistと同じ
// 「全件取得してクライアント側で探す」方式を踏襲している。
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
