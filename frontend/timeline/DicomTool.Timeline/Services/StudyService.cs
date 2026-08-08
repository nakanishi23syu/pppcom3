// ======================================================
// Services/StudyService.cs — backend/DicomTool.Api とのやり取り（検査関連）
// ======================================================
// dicom-tool-2/blazor/DicomTool.Blazor の同名ファイルの移植。GraphQLClient を土台に、
// 検査一覧・シリーズ・SOP・並べ替え・削除・復元などの個々のクエリ/ミューテーションを集約する。
//
// Timeline画面（Pages/Timeline.razor）が実際に使うのは FetchPatientTimelineAsync だけだが、
// backend側のGraphQLスキーマは検査一覧(Worklist)とタイムライン(Timeline)で共通のため、
// このファイルはWorklist向けのメソッドも含めて丸ごと移植している
// （このサービスクラス自体を「backend/DicomTool.Api の薄いクライアントSDK」として
// 素直に持ち運んだ形。Timeline側で使わないメソッドが呼ばれないだけで、実害は無い）。

using DicomTool.Timeline.Models;

namespace DicomTool.Timeline.Services;

public class StudyService
{
    private readonly GraphQLClient _client;

    public StudyService(GraphQLClient client)
    {
        _client = client;
    }

    // ------------------------------------------------------------
    // 応答をラップするための入れ子クラス群
    // GraphQLは常に { "data": { "<フィールド名>": ... } } の形で返ってくるため、
    // フィールド名に対応するプロパティを1つだけ持つラッパー型をリクエストごとに用意する。
    // ------------------------------------------------------------
    private class StudiesResponse { public List<GraphQLStudy> Studies { get; set; } = new(); }
    private class PatientTimelineResponse { public List<GraphQLStudy> PatientTimeline { get; set; } = new(); }
    private class UnreadInstancesResponse { public List<GraphQLSop> UnreadInstances { get; set; } = new(); }
    private class SeriesResponse { public GraphQLSeries? Series { get; set; } }
    private class MarkReadResponse { public GraphQLSop MarkInstanceAsRead { get; set; } = new(); }
    private class MarkUnreadResponse { public GraphQLSop MarkInstanceAsUnread { get; set; } = new(); }
    private class SaveStudyChangesResponse { public int SaveStudyChanges { get; set; } }
    private class SaveSeriesChangesResponse { public int SaveSeriesChanges { get; set; } }
    private class SaveSopChangesResponse { public int SaveSopChanges { get; set; } }
    private class RevertStudyResponse { public RevertedStudyFields RevertStudyFields { get; set; } = new(); }
    private class RevertSeriesResponse { public RevertedSeriesFields RevertSeriesFields { get; set; } = new(); }
    private class RevertSopResponse { public RevertedSopFields RevertSopFields { get; set; } = new(); }
    private class DeleteStudyResponse { public bool DeleteStudy { get; set; } }
    private class DeleteSeriesResponse { public bool DeleteSeries { get; set; } }
    private class DeleteSopResponse { public bool DeleteSop { get; set; } }

    private const string StudyFields = """
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
        """;

    public async Task<List<GraphQLStudy>> FetchStudiesAsync()
    {
        var query = $"query Studies {{ studies {{ {StudyFields} }} }}";
        var data = await _client.RequestAsync<StudiesResponse>(query);
        return data.Studies;
    }

    // Timeline.razor が使うメインのクエリ。同一患者の過去検査を新しい順にまとめて取得する。
    public async Task<List<GraphQLStudy>> FetchPatientTimelineAsync(string patientId)
    {
        var query = $$"""
            query PatientTimeline($patientId: String!) {
              patientTimeline(patientId: $patientId) { {{StudyFields}} }
            }
            """;
        var data = await _client.RequestAsync<PatientTimelineResponse>(query, new { patientId });
        return data.PatientTimeline;
    }

    public async Task<List<GraphQLSop>> FetchUnreadInstancesAsync()
    {
        const string query = """
            query UnreadInstances {
              unreadInstances {
                sopInstanceUid instanceNumber filePath isRead readAt readByUserId order
              }
            }
            """;
        var data = await _client.RequestAsync<UnreadInstancesResponse>(query);
        return data.UnreadInstances;
    }

    public async Task<GraphQLSeries?> FetchSeriesAsync(string seriesInstanceUid)
    {
        const string query = """
            query Series($seriesInstanceUid: String!) {
              series(seriesInstanceUid: $seriesInstanceUid) {
                seriesInstanceUid seriesNumber seriesDescription modality order
                sops { sopInstanceUid instanceNumber filePath isRead readAt readByUserId order }
              }
            }
            """;
        var data = await _client.RequestAsync<SeriesResponse>(query, new { seriesInstanceUid });
        return data.Series;
    }

    public async Task<GraphQLSop> MarkInstanceAsReadAsync(string sopInstanceUid, string userId)
    {
        const string query = """
            mutation MarkInstanceAsRead($sopInstanceUid: String!, $userId: String!) {
              markInstanceAsRead(sopInstanceUid: $sopInstanceUid, userId: $userId) {
                sopInstanceUid isRead readAt readByUserId
              }
            }
            """;
        var data = await _client.RequestAsync<MarkReadResponse>(query, new { sopInstanceUid, userId });
        return data.MarkInstanceAsRead;
    }

    public async Task<GraphQLSop> MarkInstanceAsUnreadAsync(string sopInstanceUid)
    {
        const string query = """
            mutation MarkInstanceAsUnread($sopInstanceUid: String!) {
              markInstanceAsUnread(sopInstanceUid: $sopInstanceUid) { sopInstanceUid isRead }
            }
            """;
        var data = await _client.RequestAsync<MarkUnreadResponse>(query, new { sopInstanceUid });
        return data.MarkInstanceAsUnread;
    }

    // ── 変更の保存（並べ替え + インライン編集の統合Mutation）──────────
    public async Task<int> SaveStudyChangesAsync(List<StudyChangeInput> changes)
    {
        const string query = """
            mutation SaveStudyChanges($changes: [StudyChangeInput!]!) {
              saveStudyChanges(changes: $changes)
            }
            """;
        var data = await _client.RequestAsync<SaveStudyChangesResponse>(query, new { changes });
        return data.SaveStudyChanges;
    }

    public async Task<int> SaveSeriesChangesAsync(List<SeriesChangeInput> changes)
    {
        const string query = """
            mutation SaveSeriesChanges($changes: [SeriesChangeInput!]!) {
              saveSeriesChanges(changes: $changes)
            }
            """;
        var data = await _client.RequestAsync<SaveSeriesChangesResponse>(query, new { changes });
        return data.SaveSeriesChanges;
    }

    public async Task<int> SaveSopChangesAsync(List<SopChangeInput> changes)
    {
        const string query = """
            mutation SaveSopChanges($changes: [SopChangeInput!]!) {
              saveSopChanges(changes: $changes)
            }
            """;
        var data = await _client.RequestAsync<SaveSopChangesResponse>(query, new { changes });
        return data.SaveSopChanges;
    }

    // ── DICOMタグへの復元 ──────────────────────────────────────────
    public async Task<RevertedStudyFields> RevertStudyFieldsAsync(string studyInstanceUid)
    {
        const string query = """
            mutation RevertStudyFields($studyInstanceUid: String!) {
              revertStudyFields(studyInstanceUid: $studyInstanceUid) {
                studyInstanceUid patientId patientName studyDate studyDescription modality accessionNumber bodyPartExamined
              }
            }
            """;
        var data = await _client.RequestAsync<RevertStudyResponse>(query, new { studyInstanceUid });
        return data.RevertStudyFields;
    }

    public async Task<RevertedSeriesFields> RevertSeriesFieldsAsync(string seriesInstanceUid)
    {
        const string query = """
            mutation RevertSeriesFields($seriesInstanceUid: String!) {
              revertSeriesFields(seriesInstanceUid: $seriesInstanceUid) {
                seriesInstanceUid seriesNumber seriesDescription modality
              }
            }
            """;
        var data = await _client.RequestAsync<RevertSeriesResponse>(query, new { seriesInstanceUid });
        return data.RevertSeriesFields;
    }

    public async Task<RevertedSopFields> RevertSopFieldsAsync(string sopInstanceUid)
    {
        const string query = """
            mutation RevertSopFields($sopInstanceUid: String!) {
              revertSopFields(sopInstanceUid: $sopInstanceUid) { sopInstanceUid instanceNumber }
            }
            """;
        var data = await _client.RequestAsync<RevertSopResponse>(query, new { sopInstanceUid });
        return data.RevertSopFields;
    }

    // ── カスケード削除 ──────────────────────────────────────────────
    public async Task DeleteStudyAsync(string studyInstanceUid)
    {
        const string query = """
            mutation DeleteStudy($studyInstanceUid: String!) { deleteStudy(studyInstanceUid: $studyInstanceUid) }
            """;
        await _client.RequestAsync<DeleteStudyResponse>(query, new { studyInstanceUid });
    }

    public async Task DeleteSeriesAsync(string seriesInstanceUid)
    {
        const string query = """
            mutation DeleteSeries($seriesInstanceUid: String!) { deleteSeries(seriesInstanceUid: $seriesInstanceUid) }
            """;
        await _client.RequestAsync<DeleteSeriesResponse>(query, new { seriesInstanceUid });
    }

    public async Task DeleteSopAsync(string sopInstanceUid)
    {
        const string query = """
            mutation DeleteSop($sopInstanceUid: String!) { deleteSop(sopInstanceUid: $sopInstanceUid) }
            """;
        await _client.RequestAsync<DeleteSopResponse>(query, new { sopInstanceUid });
    }
}
