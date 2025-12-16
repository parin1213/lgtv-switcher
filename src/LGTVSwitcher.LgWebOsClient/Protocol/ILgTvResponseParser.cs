using System.Text.Json;

namespace LGTVSwitcher.LgWebOsClient;

public interface ILgTvResponseParser
{
    /// <summary>
    /// 'getForegroundAppInfo' API のペイロード（JSON文字列）を解析し、最終的な入力 ID（HDMI_x など）を返す。
    /// </summary>
    /// <param name="payloadJson">LG TV から受け取った payload JSON。</param>
    /// <returns>入力 ID。取得できない場合は null。</returns>
    string? ParseCurrentInput(string? payloadJson);

    /// <summary>
    /// 登録 API の JSON 文字列を解析し、状態と client-key を返す。
    /// </summary>
    /// <param name="json">LG TV から受け取った生 JSON。</param>
    /// <returns>登録結果。</returns>
    LgTvRegistrationResponse ParseRegistrationResponse(string json);

    /// <summary>
    /// API 応答の生 JSON を解析してエンベロープを返す。
    /// </summary>
    LgTvResponseEnvelope ParseResponse(string? json, string requestUri);
}
