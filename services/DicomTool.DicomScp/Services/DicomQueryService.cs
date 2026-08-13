using DicomTool.Shared.Data;
using DicomTool.Shared.Entities;
using FellowOakDicom;
using Microsoft.EntityFrameworkCore;

namespace DicomTool.DicomScp.Services;

// ==========================================================================================
// DicomQueryService ― C-FIND/C-MOVEのSCP実装が共通で使う「DBへの問い合わせ」ロジック
// ==========================================================================================
// C-FINDは「マッチしたレコードのタグ一覧を返すだけ」、C-MOVEは「マッチしたレコードに対応する
// 実ファイルをC-STOREで転送する」という違いがあるだけで、「何がマッチするか」を決めるロジック
// （検索条件の解釈）は完全に共通のため、1つのクラスに集約してDicomScpServiceから両方が使う。
//
// このプロジェクトの学習用スコープとして、STUDY階層とSERIES階層のQuery/Retrieveのみ対応する
// (PATIENT/IMAGE階層は非対応。docs/dicom-testing-tools/dcmtk.md参照)。
public interface IDicomQueryService
{
    /// <summary>
    /// STUDY階層のC-FIND/C-MOVE要求データセットから検索条件を読み取り、マッチするUserStudyを
    /// (Series/Sopsも含めて)返す。
    /// </summary>
    Task<List<UserStudy>> FindStudiesAsync(DicomDataset queryDataset, CancellationToken cancellationToken);

    /// <summary>
    /// SERIES階層のC-FIND/C-MOVE要求データセットから検索条件を読み取り、マッチするUserSeriesを
    /// (親Study・Sopsも含めて)返す。DICOMの作法上、SERIES階層の検索にはStudyInstanceUIDが
    /// 必須級の絞り込みキーだが、このプロジェクトでは省略時は全Study横断で検索する(緩め)。
    /// </summary>
    Task<List<UserSeries>> FindSeriesAsync(DicomDataset queryDataset, CancellationToken cancellationToken);
}

public sealed class DicomQueryService : IDicomQueryService
{
    private readonly DicomDbContext _db;

    public DicomQueryService(DicomDbContext db)
    {
        _db = db;
    }

    public async Task<List<UserStudy>> FindStudiesAsync(DicomDataset queryDataset, CancellationToken cancellationToken)
    {
        // Series・Sopsという2段のコレクションを一度にIncludeすると、EF Coreは既定で
        // 「1本のJOINクエリ」に展開しようとし、行数がSeries×Sopsの掛け算で膨らむ
        // (=同じStudyの行が何度も重複して返る「デカルト積」)。AsSplitQuery()を使うと
        // Study本体とSeries+Sopsを別々のSELECT文に分けて発行し、この膨張を避けられる
        // (EF Coreがこの選択を要求する警告を出すため、それに従って明示した)。
        var query = _db.UserStudies
            .AsSplitQuery()
            .Include(s => s.Series)
            .ThenInclude(se => se.Sops)
            .AsQueryable();

        query = ApplyLikeFilter(query, s => s.PatientId, queryDataset, DicomTag.PatientID);
        query = ApplyLikeFilter(query, s => s.PatientName, queryDataset, DicomTag.PatientName);
        query = ApplyLikeFilter(query, s => s.AccessionNumber, queryDataset, DicomTag.AccessionNumber);
        query = ApplyLikeFilter(query, s => s.StudyDescription, queryDataset, DicomTag.StudyDescription);
        query = ApplyLikeFilter(query, s => s.Modality, queryDataset, DicomTag.ModalitiesInStudy);

        // StudyInstanceUIDはDICOM上「一意なUID」であり、ワイルドカードを使わない完全一致が原則のため
        // LIKEではなく単純な等価比較にする(値が空欄なら絞り込みしない＝全件対象、という通常のC-FINDの作法)。
        var studyInstanceUid = queryDataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, "");
        if (!string.IsNullOrEmpty(studyInstanceUid))
        {
            query = query.Where(s => s.StudyInstanceUid == studyInstanceUid);
        }

        return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<UserSeries>> FindSeriesAsync(DicomDataset queryDataset, CancellationToken cancellationToken)
    {
        var query = _db.UserSeries
            .Include(se => se.Study)
            .Include(se => se.Sops)
            .AsQueryable();

        var studyInstanceUid = queryDataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, "");
        if (!string.IsNullOrEmpty(studyInstanceUid))
        {
            query = query.Where(se => se.Study != null && se.Study.StudyInstanceUid == studyInstanceUid);
        }

        var seriesInstanceUid = queryDataset.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, "");
        if (!string.IsNullOrEmpty(seriesInstanceUid))
        {
            query = query.Where(se => se.SeriesInstanceUid == seriesInstanceUid);
        }

        query = ApplyLikeFilter(query, se => se.SeriesDescription, queryDataset, DicomTag.SeriesDescription);
        query = ApplyLikeFilter(query, se => se.Modality, queryDataset, DicomTag.Modality);

        return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    // DICOMの検索キーは "*"(任意文字列)・"?"(任意1文字)というワイルドカードをサポートする
    // (PS3.4 C.2.2.2.4)。SQLのLIKE構文とワイルドカード文字が違うだけで考え方は同じなので、
    // "*"→"%"、"?"→"_" に変換してEF Core経由でDB側のLIKEに翻訳させる(インメモリ全件取得を避けるため)。
    // クエリ要素が無い(=そのタグを検索条件に指定していない)場合は絞り込みをスキップする
    // (DICOMの作法上、値が空の要素は「その項目を結果に含めてほしい」という意味で、
    //  絞り込み条件ではないことに注意)。
    private static IQueryable<T> ApplyLikeFilter<T>(
        IQueryable<T> query,
        System.Linq.Expressions.Expression<Func<T, string>> propertySelector,
        DicomDataset queryDataset,
        DicomTag tag)
    {
        var rawValue = queryDataset.GetSingleValueOrDefault(tag, "");
        if (string.IsNullOrEmpty(rawValue))
        {
            return query;
        }

        var likePattern = rawValue.Replace("*", "%").Replace("?", "_");
        var parameter = propertySelector.Parameters[0];
        var property = propertySelector.Body;

        var likeMethod = typeof(DbFunctionsExtensions).GetMethod(
            nameof(DbFunctionsExtensions.Like), [typeof(DbFunctions), typeof(string), typeof(string)])!;
        var efFunctions = System.Linq.Expressions.Expression.Constant(EF.Functions);
        var patternConstant = System.Linq.Expressions.Expression.Constant(likePattern);
        var call = System.Linq.Expressions.Expression.Call(likeMethod, efFunctions, property, patternConstant);
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(call, parameter);

        return query.Where(lambda);
    }
}
