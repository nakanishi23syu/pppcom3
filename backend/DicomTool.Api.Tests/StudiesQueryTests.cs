using DicomTool.Api.Tests.Infrastructure;
using DicomTool.Shared.Data;
using DicomTool.Shared.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DicomTool.Api.Tests;

// ============================================================================
// StudiesQueryTests ―― studies Query（GraphQL/Query.cs の GetStudiesAsync）の結合テスト
// ============================================================================
// 【DBへの「事前データ投入」について】
// このテストでは、GraphQL越しではなく、DbContextを直接使ってInMemory DBへ
// テスト用のデータ（UserStudy/UserSeries/UserSop）を先に書き込んでから、
// studiesクエリを呼び出して「投入した内容が正しく返ってくるか」を確認する。
// 「本物のPostgreSQLではなくInMemoryプロバイダを使っているからこそ、
// テストコードから直接DbContextを触って自由に前提データを用意できる」というのも、
// InMemoryプロバイダを使う実用上のメリットの1つ。
public class StudiesQueryTests : IClassFixture<DicomToolWebApplicationFactory>
{
    private readonly DicomToolWebApplicationFactory _factory;

    public StudiesQueryTests(DicomToolWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Studies_ReturnsSeededStudyWithSeriesAndSop()
    {
        // ------------------------------------------------------------------
        // 1. 事前データ投入
        // ------------------------------------------------------------------
        // _factory.Services は、WebApplicationFactoryが構築したDIコンテナそのもの
        // （DicomToolWebApplicationFactory.ConfigureWebHost で InMemory プロバイダに
        // 差し替え済みのDicomDbContextが登録されている）。
        // DbContextはScopedな寿命のクラスなので、DIコンテナから直接1個だけ取り出すのではなく、
        // CreateScope()でリクエスト1回分相当の「スコープ」を作ってから取り出すのがお作法
        // （Program.cs起動時のシード処理も同じパターンを使っている）。
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DicomDbContext>();

            var study = new UserStudy
            {
                StudyInstanceUid = "1.2.840.test.study.001",
                PatientId = "patient-test-001",
                PatientName = "テスト 太郎",
                StudyDate = new DateOnly(2026, 1, 15),
                StudyDescription = "結合テスト用の検査",
                Modality = "CT",
                Order = 0,
            };
            var series = new UserSeries
            {
                SeriesInstanceUid = "1.2.840.test.series.001",
                SeriesNumber = "1",
                SeriesDescription = "結合テスト用シリーズ",
                Modality = "CT",
                Order = 0,
            };
            var sop = new UserSop
            {
                SopInstanceUid = "1.2.840.test.sop.001",
                FilePath = "1.2.840.test.study.001/1.2.840.test.series.001/1.2.840.test.sop.001.dcm",
                InstanceNumber = "1",
                Order = 0,
            };
            series.Sops.Add(sop);
            study.Series.Add(series);

            db.UserStudies.Add(study);
            await db.SaveChangesAsync();
        }

        // ------------------------------------------------------------------
        // 2. GraphQL越しに studies クエリを呼び、投入した内容が返ってくるか確認する
        // ------------------------------------------------------------------
        var client = _factory.CreateClient();
        var response = await client.PostGraphQLAsync(
            """
            query {
              studies {
                studyInstanceUid
                patientId
                patientName
                studyDescription
                modality
                series {
                  seriesInstanceUid
                  seriesDescription
                  sops {
                    sopInstanceUid
                    instanceNumber
                  }
                }
              }
            }
            """);

        Assert.False(response.HasErrors(), response.FirstErrorMessage());

        var studies = response["data"]!["studies"]!.AsArray();
        Assert.Single(studies);

        var returnedStudy = studies[0]!;
        Assert.Equal("1.2.840.test.study.001", returnedStudy["studyInstanceUid"]!.GetValue<string>());
        Assert.Equal("patient-test-001", returnedStudy["patientId"]!.GetValue<string>());
        Assert.Equal("テスト 太郎", returnedStudy["patientName"]!.GetValue<string>());
        Assert.Equal("CT", returnedStudy["modality"]!.GetValue<string>());

        var returnedSeries = returnedStudy["series"]!.AsArray();
        Assert.Single(returnedSeries);
        Assert.Equal("1.2.840.test.series.001", returnedSeries[0]!["seriesInstanceUid"]!.GetValue<string>());

        var returnedSops = returnedSeries[0]!["sops"]!.AsArray();
        Assert.Single(returnedSops);
        Assert.Equal("1.2.840.test.sop.001", returnedSops[0]!["sopInstanceUid"]!.GetValue<string>());
    }
}
