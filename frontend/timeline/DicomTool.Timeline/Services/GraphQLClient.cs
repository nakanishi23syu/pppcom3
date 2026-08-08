// ======================================================
// Services/GraphQLClient.cs — GraphQLサーバーへの汎用リクエストクラス
// ======================================================
// dicom-tool-2/blazor/DicomTool.Blazor の同名ファイルの移植。
//
// 【GraphQLの呼び方の基本形】
// RESTのように「URLを変えて呼び分ける」のではなく、常に同じエンドポイントへ POST し、
// body に query（GraphQLクエリ言語の文字列）と variables（$変数への実値）を JSON で入れて送るだけ。
//
// 【Blazor WASMでいちばんハマりやすい注意点: Cookieが既定では送られない】
// Blazor WASMの HttpClient は内部的にブラウザの fetch API を使って通信するが、
// 既定では fetch の `credentials` オプションが 'same-origin' 相当になっており、
// 別オリジン（http://localhost:5030、backend/DicomTool.Api）への Cookie 送信は行われない。
// dicom-tool-3では Worklist(3100)・Viewer(3200)・Timeline(5230) の3オリジンがそれぞれ
// backend/DicomTool.Api(5030) にアクセスするため、この問題はTimelineに限らず全フロントで
// 共通して踏む可能性がある罠だが、Timelineとしてはこの GraphQLClient にだけ集約しておけばよい。
//
// Blazor は `Microsoft.AspNetCore.Components.WebAssembly.Http` 名前空間の
// 拡張メソッド `HttpRequestMessage.SetBrowserRequestCredentials(...)` でこれを制御できる。
// これを呼び忘れると、ログインは成功する（Set-Cookieレスポンスヘッダー自体は届く）のに
// 以後のリクエストにCookieが乗らず、常に未ログイン扱いになる…という分かりにくい不具合になる。
// 本プロジェクトで最初に必ず踏むべき罠なので、下記 RequestAsync 内に集約して二度と忘れないようにする。
// （Timeline自体は認証不要のクエリしか呼ばないため今のところ影響は無いが、backendのCookie認証と
// 組み合わせて使う他ページを将来このプロジェクトに追加する場合に備え、移植元のまま残している）。

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using Microsoft.Extensions.Configuration;

namespace DicomTool.Timeline.Services;

/// <summary>
/// HotChocolateのGraphQLエラー1件分（errors[]の要素）。
/// 認証エラー・認可エラーは同じメッセージ文言になることがあるため、
/// extensions.code（例: AUTH_NOT_AUTHENTICATED）で区別できるよう保持しておく。
/// </summary>
public class GraphQLError
{
    public string Message { get; set; } = "";
    public GraphQLErrorExtensions? Extensions { get; set; }
}

public class GraphQLErrorExtensions
{
    public string? Code { get; set; }
}

internal class GraphQLResponse<T>
{
    public T? Data { get; set; }
    public List<GraphQLError>? Errors { get; set; }
}

/// <summary>
/// GraphQLのerrors[]をラップした例外。
/// </summary>
public class GraphQLRequestException : Exception
{
    /// <summary>各エラーの extensions.code の一覧（無いものは除外済み）</summary>
    public IReadOnlyList<string> Codes { get; }

    public GraphQLRequestException(string message, IReadOnlyList<string> codes) : base(message)
    {
        Codes = codes;
    }
}

/// <summary>
/// GraphQLサーバーへの汎用POSTヘルパー。DIでScopedとして登録し、各Service（StudyService等）が
/// これを介して個々のクエリ・ミューテーションを呼び出す。
/// </summary>
public class GraphQLClient
{
    private readonly HttpClient _http;

    // GraphQLのフィールド名（camelCase）をC#のPascalCaseプロパティに素直にマッピングするための設定。
    // これを共通化しておくことで、Models/GraphQLModels.cs の各DTOに [JsonPropertyName] を
    // 1つずつ書かずに済む。
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    // backend/DicomTool.Api のGraphQLエンドポイント。ポート5030は
    // shared/DicomTool.Shared/Constants/ServicePorts.cs の BackendApiHttp と同じ値
    // （docs/CONTRACT.md 1章が唯一の正。値を変える場合はそちらを更新してからここを追従させる）。
    // Blazor WebAssemblyはブラウザ上でそのまま実行されるため、backendが別マシン
    // （例: VM）上にある場合はlocalhostでは届かない。wwwroot/appsettings.jsonの
    // GraphQLEndpoint/DicomFileBaseUrlで上書きできるようにし、既定値はローカルdocker-compose
    // 環境向けのlocalhostのままにしてある。
    public readonly string Endpoint;
    public readonly string DicomFileBaseUrl;

    public GraphQLClient(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        Endpoint = configuration["GraphQLEndpoint"] ?? "http://localhost:5030/graphql";
        DicomFileBaseUrl = configuration["DicomFileBaseUrl"] ?? "http://localhost:5030/dicom-files";
    }

    /// <summary>
    /// GraphQLクエリ/ミューテーションを実行し、data部分をT型として返す。
    /// errors[]が含まれていれば GraphQLRequestException を投げる。
    /// </summary>
    public async Task<T> RequestAsync<T>(string query, object? variables = null)
    {
        var payload = new { query, variables };

        // 通常の PostAsJsonAsync ではなく、あえて HttpRequestMessage を自前で組み立てているのは
        // SetBrowserRequestCredentials(Include) を呼ぶため（上記コメント参照）。
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(payload),
        };

        // ==========================================================
        // ここがBlazor WASM + Cookie認証でいちばん重要な1行。
        // BrowserRequestCredentials.Include を指定することで、fetch の
        // `credentials: 'include'` と同じ効果になり、別オリジンへのリクエストにも
        // Cookie（dicom_auth_token）が付与されるようになる。
        // ==========================================================
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);

        var response = await _http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"GraphQLサーバーへの通信に失敗しました (HTTP {(int)response.StatusCode})");
        }

        var json = await response.Content.ReadFromJsonAsync<GraphQLResponse<T>>(JsonOptions);

        if (json is null)
        {
            throw new InvalidOperationException("GraphQLのレスポンスを読み取れませんでした");
        }

        if (json.Errors is { Count: > 0 })
        {
            var message = string.Join(", ", json.Errors.Select(e => e.Message));
            var codes = json.Errors
                .Select(e => e.Extensions?.Code)
                .Where(c => !string.IsNullOrEmpty(c))
                .Select(c => c!)
                .ToList();
            throw new GraphQLRequestException(message, codes);
        }

        if (json.Data is null)
        {
            throw new InvalidOperationException("GraphQLのレスポンスにdataが含まれていません");
        }

        return json.Data;
    }
}
