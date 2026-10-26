using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;

namespace GravewardSpiral.Core.Ai;

/// <summary>
/// Claude API の窓口(公式の C# SDK)。返事の形は structured outputs で決める。
/// 断られたときは、サーバー側の代わりのモデル(fallbacks)に回してもらう。
/// </summary>
public sealed class ClaudeClient : IAiClient
{
    public const string DefaultModel = "claude-sonnet-5-5";   // 世界の文は Sonnet がいちばん良かった(Opus・Haiku と同じ世界で比べた)

    /// <summary>選べるモデル(ゲームの AI 設定とシナリオエディタで同じもの)。</summary>
    public static readonly string[] Models = ["claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5"];

    private readonly AnthropicClient _client;

    public ClaudeClient(string apiKey, string? model = null)
    {
        _client = new AnthropicClient { ApiKey = apiKey };
        Model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
    }

    public string Model { get; }

    public async Task<AiJsonReply> GenerateJsonAsync(AiJsonRequest request, CancellationToken cancel)
    {
        var schema = request.Schema.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        var parameters = new MessageCreateParams
        {
            Model = Model,
            MaxTokens = request.MaxTokens,
            Betas = ["server-side-fallback-2026-07-01"],
            Fallbacks = new Default(),   // 断られたら、断りの種類に合わせてサーバーが代わりのモデルに回す
            System = request.Cached is { } cached
                ? new List<BetaTextBlockParam> { new() { Text = request.System }, new() { Text = cached, CacheControl = new BetaCacheControlEphemeral() } }
                : request.System,
            OutputConfig = new BetaOutputConfig
            {
                Effort = Effort.Medium,
                Format = new BetaJsonOutputFormat { Schema = schema },
            },
            Messages = [new BetaUserMessageParam { Content = request.User }],
        };
        try
        {
            var response = await _client.Beta.Messages.Create(parameters, cancel);
            if (response.StopReason == "refusal") return AiJsonReply.Fail("refusal");
            if (response.StopReason == "max_tokens") return AiJsonReply.Fail("max_tokens");
            string text = string.Concat(response.Content.Select(b => b.TryPickText(out var t) ? t.Text : ""));
            using var doc = JsonDocument.Parse(text);
            return new AiJsonReply(doc.RootElement.Clone(), null, response.Usage.InputTokens, response.Usage.OutputTokens,
                response.Usage.CacheReadInputTokens ?? 0, response.Usage.CacheCreationInputTokens ?? 0);
        }
        catch (OperationCanceledException)
        {
            return AiJsonReply.Fail("canceled");
        }
        catch (AnthropicUnauthorizedException)
        {
            return AiJsonReply.Fail("unauthorized");
        }
        catch (AnthropicNotFoundException)
        {
            return AiJsonReply.Fail("model_not_found");
        }
        catch (AnthropicRateLimitException)
        {
            return AiJsonReply.Fail("rate_limit");
        }
        catch (AnthropicApiException ex)
        {
            return AiJsonReply.Fail($"api: {ex.Message}");
        }
        catch (AnthropicIOException ex)
        {
            return AiJsonReply.Fail($"network: {ex.Message}");
        }
        catch (JsonException)
        {
            return AiJsonReply.Fail("bad_json");
        }
    }
}
