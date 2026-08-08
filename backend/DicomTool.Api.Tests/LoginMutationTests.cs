using DicomTool.Api.Tests.Infrastructure;
using Xunit;

namespace DicomTool.Api.Tests;

// ============================================================================
// LoginMutationTests ―― login Mutation（GraphQL/Mutation.cs の LoginAsync）の結合テスト
// ============================================================================
// 【IClassFixture<DicomToolWebApplicationFactory> とは】
// xUnitの仕組みで、「このテストクラス内の全テストメソッドで、1つの
// DicomToolWebApplicationFactoryインスタンス（＝1つの疑似ASP.NET Coreアプリ、
// 1つのInMemory DB）を使い回す」ことを表す。テストメソッドごとに毎回アプリを
// 起動し直すと時間がかかる（DIコンテナの構築等）ため、同じクラス内のテスト同士で
// 副作用が問題にならない範囲では使い回すのが定石になっている。
// （このクラスの各テストは admin/dr-tanaka という起動時シード済みユーザーを
// 参照するだけで、互いにデータを書き換え合わないため、使い回して問題ない。）
public class LoginMutationTests : IClassFixture<DicomToolWebApplicationFactory>
{
    private readonly DicomToolWebApplicationFactory _factory;

    public LoginMutationTests(DicomToolWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_WithCorrectCredentials_ReturnsUserInfo()
    {
        // _factory.CreateClient() は、WebApplicationFactoryが起動した「メモリ上のASP.NET Core
        // アプリ」に対してHTTPリクエストを送るための、普通のHttpClientそのもの
        // （実際にlocalhostの何番ポートにも繋いでいない点だけが、普段使うHttpClientと違う）。
        var client = _factory.CreateClient();

        // backend/DicomTool.Api/Data/DbSeeder.cs が、アプリ起動時（＝WebApplicationFactoryが
        // このテストのためにアプリを起動したとき）にInMemory DBへ自動で投入している
        // "admin" / "admin1234" アカウントでログインを試す。
        var response = await client.PostGraphQLAsync(
            """
            mutation($username: String!, $password: String!) {
              login(username: $username, password: $password) {
                displayName
                isAdmin
              }
            }
            """,
            new { username = "admin", password = "admin1234" });

        Assert.False(response.HasErrors(), response.FirstErrorMessage());

        var login = response["data"]!["login"]!;
        Assert.Equal("管理者", login["displayName"]!.GetValue<string>());
        Assert.True(login["isAdmin"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsError()
    {
        var client = _factory.CreateClient();

        var response = await client.PostGraphQLAsync(
            """
            mutation($username: String!, $password: String!) {
              login(username: $username, password: $password) {
                displayName
              }
            }
            """,
            new { username = "admin", password = "wrong-password" });

        // Mutation.cs の LoginAsync は、認証に失敗すると
        // `throw new GraphQLException("ユーザー名またはパスワードが正しくありません。");`
        // でエラーを投げる。GraphQLはこれをHTTP 200 + レスポンスボディの"errors"配列として返す
        // （RESTの401/403のようにHTTPステータスコードでは表現しない）ため、
        // テストでもステータスコードではなく "errors" フィールドの有無で判定する。
        Assert.True(response.HasErrors());
        Assert.Contains("正しくありません", response.FirstErrorMessage());
    }

    [Fact]
    public async Task Login_WithUnknownUsername_ReturnsError()
    {
        var client = _factory.CreateClient();

        var response = await client.PostGraphQLAsync(
            """
            mutation($username: String!, $password: String!) {
              login(username: $username, password: $password) {
                displayName
              }
            }
            """,
            new { username = "no-such-user", password = "whatever" });

        Assert.True(response.HasErrors());
    }
}
