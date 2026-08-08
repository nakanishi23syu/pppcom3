namespace DicomTool.Api.Constants;

// ======================================================
// AppConstants — アプリ全体で使う定数
// ======================================================
// マジックストリングをあちこちに書くと、片方だけ直し忘れて動かなくなる事故が起きやすい。
// 複数箇所（Program.cs の AddCors と UseCors 等）で同じ文字列を使うものはここに集約する。
//
// ポート番号やTemporal/DICOMの識別子のような「サービス間で合意すべき値」は
// shared/DicomTool.Shared/Constants/ 側に定義されている（docs/CONTRACT.md参照）。
// ここに置くのはこのApiプロジェクト内部だけで完結する定数に限る。
internal static class AppConstants
{
    // フロントエンド用CORSポリシーの名前。
    public const string FrontendCorsPolicy = "FrontendCorsPolicy";

    // JWTを保持するhttpOnly Cookieの名前。
    // JWTをレスポンスボディで返しフロントエンドがlocalStorageに保存する方式だと、
    // XSS（悪意あるスクリプトの混入）が起きた場合にlocalStorageは任意のJSから読み放題のため
    // トークンを盗まれるリスクがある。httpOnly Cookieはブラウザが自動送信し、
    // JavaScriptからは（document.cookie経由でも）読み書きできないため、その経路を塞げる。
    public const string AuthCookieName = "dicom_auth_token";
}
