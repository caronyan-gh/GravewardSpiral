using System.Text.Json;

namespace GravewardSpiral.Core.Ai;

/// <summary>AI に頼むこと1つ。返事は schema の形の JSON で受け取る。</summary>
public sealed record AiJsonRequest(string System, string User, JsonElement Schema, int MaxTokens = 16000);

/// <summary>AI の返事。Json が null なら失敗で、Error に理由(画面に出す短い文)が入る。</summary>
public sealed record AiJsonReply(JsonElement? Json, string? Error, long InputTokens = 0, long OutputTokens = 0)
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
