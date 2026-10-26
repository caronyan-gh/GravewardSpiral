using System.Text.Json;

namespace GravewardSpiral.Core.Ai;

/// <summary>
/// AI に頼むこと1つ。返事は schema の形の JSON で受け取る。
/// Cached があれば、system の後ろに置いて、そこまでをプロンプトキャッシュにする(毎回同じ前置き。同じものを5分以内にまた送ると、その分が約 1/10 の値段になる)。
/// </summary>
public sealed record AiJsonRequest(string System, string User, JsonElement Schema, int MaxTokens = 16000, string? Cached = null);

/// <summary>
/// AI の返事。Json が null なら失敗で、Error に理由(画面に出す短い文)が入る。
/// InputTokens はキャッシュを使わずに読んだ分、CacheRead はキャッシュから読んだ分、CacheWrite はキャッシュに書いた分。
/// </summary>
public sealed record AiJsonReply(JsonElement? Json, string? Error, long InputTokens = 0, long OutputTokens = 0, long CacheRead = 0, long CacheWrite = 0)
{
    public static AiJsonReply Fail(string error) => new(null, error);
}

/// <summary>
/// AI の窓口。Claude API と OpenAI 互換の API を同じ形で呼べるようにする(今は Claude だけ)。
/// 例外は投げず、失敗は AiJsonReply.Error で返す(呼ぶ側は決まった文で埋める)。
/// </summary>
public interface IAiClient
{
    /// <summary>記録に残すモデルの名前。</summary>
    string Model { get; }

    Task<AiJsonReply> GenerateJsonAsync(AiJsonRequest request, CancellationToken cancel);
}
