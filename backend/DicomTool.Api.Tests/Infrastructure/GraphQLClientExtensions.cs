using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DicomTool.Api.Tests.Infrastructure;

// ============================================================================
// GraphQLClientExtensions ―― テストからGraphQLエンドポイントを呼ぶための小さな共通処理
// ============================================================================
// GraphQLは技術的には「1本のHTTPエンドポイント(/graphql)にPOSTでJSONを送るAPI」に過ぎない。
// このヘルパーは「query/mutation文字列と変数をJSONに組み立ててPOSTし、
// レスポンスJSONを System.Text.Json.Nodes.JsonNode として返す」だけの薄いラッパー。
// GraphQL専用のクライアントライブラリを別途導入しなくても、素のHttpClientで十分テストできる
// ことを示す意図もある（学習用に、あえて「GraphQLの生のプロトコル」がどう見えるかを残している）。
internal static class GraphQLClientExtensions
{
    /// <summary>
    /// GraphQLのquery/mutationを実行し、レスポンスボディ全体（"data"と"errors"を含むJSON）を返す。
    /// ファイルアップロードを伴わない、通常のquery/mutation用。
    /// </summary>
    public static async Task<JsonNode> PostGraphQLAsync(
        this HttpClient client,
        string query,
        object? variables = null)
    {
        var response = await client.PostAsJsonAsync("/graphql", new { query, variables });
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return JsonNode.Parse(json) ?? throw new InvalidOperationException("GraphQLレスポンスのJSON解析に失敗しました。");
    }

    /// <summary>
    /// レスポンスJSONの "errors" 配列が存在するかどうか。
    /// GraphQLはHTTPステータスとしては200を返しつつ、エラーは"errors"フィールドで表現する
    /// （REST APIの4xx/5xxとは違う流儀なので、テストでもここを見て判定する必要がある）。
    /// </summary>
    public static bool HasErrors(this JsonNode responseBody) =>
        responseBody["errors"] is JsonArray { Count: > 0 };

    /// <summary>
    /// "errors"配列内の最初のメッセージを取り出す（アサーションのエラーメッセージ表示用）。
    /// </summary>
    public static string? FirstErrorMessage(this JsonNode responseBody) =>
        responseBody["errors"]?[0]?["message"]?.GetValue<string>();
}
