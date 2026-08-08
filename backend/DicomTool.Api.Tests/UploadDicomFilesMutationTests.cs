using System.Text.Json;
using DicomTool.Api.Tests.Infrastructure;
using DicomTool.Shared.Constants;
using NSubstitute;
using Temporalio.Client;
using Xunit;

namespace DicomTool.Api.Tests;

// ============================================================================
// UploadDicomFilesMutationTests ―― uploadDicomFiles Mutation の結合テスト
// ============================================================================
// 【このテストで確認したいこと】
// backend/DicomTool.Api/GraphQL/Mutation.cs の UploadDicomFilesAsync は、
//   1. アップロードされたファイルをステージング領域(infra/data/dicom-incoming/)に書き込む
//   2. Temporal ClientでUploadDicomWorkflowを起動する（実際のDICOM解析やDB登録は
//      services/DicomTool.Worker が非同期に行うので、このMutation自体はそこまで行わない）
// という2つだけを行う（詳細な設計理由はMutation.cs、DicomUploadAcceptedResult.csのコメント参照）。
// つまりここで検証すべきなのは「本当にファイルがDICOMとして正しいか」でも
// 「ワークフローが最後まで正常に完了するか」でもなく、
//   - GraphQL経由でファイルを送ると accepted: true が返ってくること
//   - ITemporalClient.StartWorkflowAsync が「正しい引数」（ワークフロー種別名・タスクキュー名）で
//     ちょうど1回呼ばれたこと
// の2点。後者を「本物のTemporal Serverを一切使わずに」確認できるのが、
// DicomToolWebApplicationFactory で用意したモック(TemporalClientMock)の効能そのもの
// （本物のサーバーに繋いでいたら、「呼び出しの引数」を確認するだけのために、毎回ネットワーク越しの
// 実行を待つ必要があり、テストが遅く・不安定になっていたはず）。
//
// 「実際にWorkerがファイルを取り込めるか」「取り込んだ結果が検査一覧に反映されるか」といった
// "本物を通した"最終確認は、このプロジェクトの役割ではなく tests/e2e/test_upload.py
// （Playwrightを使ったE2Eテスト）が担当する。
public class UploadDicomFilesMutationTests : IClassFixture<DicomToolWebApplicationFactory>
{
    private readonly DicomToolWebApplicationFactory _factory;

    public UploadDicomFilesMutationTests(DicomToolWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UploadDicomFiles_AcceptsFileAndStartsWorkflowExactlyOnce()
    {
        var client = _factory.CreateClient();

        // ------------------------------------------------------------------
        // GraphQL Multipart Request にそったリクエストを、素のHttpClientで組み立てる
        // ------------------------------------------------------------------
        // ブラウザ(フロントエンド)が使っているライブラリは裏側でこれと同じ形のHTTPリクエストを
        // 組み立てている。仕様: https://github.com/jaydenseric/graphql-multipart-request-spec
        //   - "operations" パート: 通常のGraphQLリクエストJSON。ファイルを渡す変数の値は
        //     いったんnullにしておく（実体は別パートで送るため）。
        //   - "map" パート: "0番目の追加パート" が "variables.files.0" （＝operations内の
        //     variables.files配列の0番目）に対応する、という対応表。
        //   - "0" パート: ファイルの実データそのもの。
        // このMutationはファイルの中身を解析しない（fo-dicomでのパースはWorker側の責務）ため、
        // テストでは実物のDICOMファイルを用意せず、ダミーのバイト列で十分。
        const string query = """
            mutation($files: [Upload!]!) {
              uploadDicomFiles(files: $files) {
                fileName
                accepted
                workflowId
                message
              }
            }
            """;
        var operations = JsonSerializer.Serialize(new
        {
            query,
            variables = new { files = new object?[] { null } },
        });
        var map = JsonSerializer.Serialize(new Dictionary<string, string[]>
        {
            ["0"] = ["variables.files.0"],
        });

        using var form = new MultipartFormDataContent
        {
            { new StringContent(operations), "operations" },
            { new StringContent(map), "map" },
        };
        var dummyFileBytes = "DICM-DUMMY-CONTENT-FOR-TEST"u8.ToArray();
        var fileContent = new ByteArrayContent(dummyFileBytes);
        form.Add(fileContent, "0", "sample.dcm");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/graphql") { Content = form };
        // HotChocolateは、マルチパート(ファイルアップロード)形式のGraphQLリクエストに対して
        // "GraphQL-Preflight" ヘッダーを必須にしている（CSRF対策。単純なHTMLフォームからの
        // 送信ではこのカスタムヘッダーを付けられないため、悪意あるサイトからの
        // 「知らないうちにログイン中のブラウザ経由でファイルをアップロードさせられる」
        // 攻撃を防いでいる）。フロントエンドのGraphQLクライアントも内部でこのヘッダーを
        // 自動的に付けており、テストでも同様に明示的に付ける必要がある。
        request.Headers.Add("GraphQL-Preflight", "1");

        var httpResponse = await client.SendAsync(request);
        var rawBody = await httpResponse.Content.ReadAsStringAsync();
        // レスポンス本文もアサーションの失敗メッセージに含めておくと、テストが落ちたときに
        // 「なぜ落ちたか」（GraphQLサーバーが返したエラーメッセージ）がテスト結果の画面だけで
        // わかるようになる。
        Assert.True(httpResponse.IsSuccessStatusCode, $"status={httpResponse.StatusCode} body={rawBody}");

        var body = System.Text.Json.Nodes.JsonNode.Parse(rawBody)!;
        Assert.False(body.HasErrors(), body.FirstErrorMessage());

        var result = body["data"]!["uploadDicomFiles"]![0]!;
        Assert.Equal("sample.dcm", result["fileName"]!.GetValue<string>());
        Assert.True(result["accepted"]!.GetValue<bool>());
        Assert.False(string.IsNullOrEmpty(result["workflowId"]!.GetValue<string>()));

        // ------------------------------------------------------------------
        // モックの検証 ―― 「本物のTemporalへ接続せずに」ここまで確認できるのがモックの価値
        // ------------------------------------------------------------------
        // NSubstituteの `Received(1)` は「このメソッドが、指定した引数の条件に一致する形で
        // ちょうど1回呼ばれたこと」を検証する。呼ばれていなかったり、2回以上呼ばれていたり、
        // 引数が条件に合わなかったりすると、このAssertの時点でテストが失敗する。
        // Arg.Is<string>(...) / Arg.Any<T>() はNSubstituteの「引数マッチャー」で、
        // 「厳密に同じインスタンスか」ではなく「条件を満たす値であればOK」という緩い比較を書ける。
        await _factory.TemporalClientMock.Received(1).StartWorkflowAsync(
            Arg.Is<string>(workflowType => workflowType == TemporalConstants.UploadDicomWorkflowTypeName),
            Arg.Any<IReadOnlyCollection<object>>(),
            Arg.Is<WorkflowOptions>(options => options.TaskQueue == TemporalConstants.TaskQueue));
    }
}
