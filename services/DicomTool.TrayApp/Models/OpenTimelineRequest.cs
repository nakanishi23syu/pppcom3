namespace DicomTool.TrayApp.Models;

/// <summary>
/// POST /commands/open-timeline のリクエストボディ。
/// Worklist(frontend/worklist, ポート3100)が「この患者のTimelineを開いてほしい」と
/// 依頼してくる際に、対象の患者IDだけを渡してくる想定のシンプルなDTO。
///
/// Minimal APIではプリミティブ型でないパラメータは既定でJSONボディから
/// バインドされるため、[FromBody]属性は不要（record自体がそのままモデルになる）。
/// </summary>
/// <param name="PatientId">Timelineで表示したい患者のID。</param>
public sealed record OpenTimelineRequest(string? PatientId);
