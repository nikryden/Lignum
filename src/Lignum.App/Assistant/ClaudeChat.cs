using System.Text.Json;
using Anthropic;
using Anthropic.Helpers;
using Anthropic.Models.Beta.Messages;

namespace Lignum.App.Assistant;

/// <summary>One tool call Claude asked for: its id, tool name and JSON input.</summary>
public sealed record ToolCall(string Id, string Name, IReadOnlyDictionary<string, JsonElement> Input);

/// <summary>
/// One assistant turn: its text, the tool calls it asked for, why it stopped, and the content to send back
/// unchanged as the assistant message of the history (thinking blocks keep their signatures).
/// </summary>
public sealed record AssistantReply(string Text, IReadOnlyList<ToolCall> ToolCalls, string StopReason,
                                   List<BetaContentBlockParam> Echo);

/// <summary>Sends one request to Claude (ticket 05). Tests replace it with a scripted fake.</summary>
public interface IClaudeChat
{
    /// <summary>Streams one turn; <paramref name="onText"/> gets each text fragment as it arrives.</summary>
    Task<AssistantReply> SendAsync(MessageCreateParams request, Action<string> onText, CancellationToken cancellationToken);
}

/// <summary>The real Claude API, through the official Anthropic SDK, streamed.</summary>
public sealed class AnthropicClaudeChat(string? apiKey) : IClaudeChat
{
    // Null key: the SDK reads ANTHROPIC_API_KEY (or the user's `ant auth login` profile).
    private readonly AnthropicClient _client = apiKey is null ? new AnthropicClient() : new AnthropicClient { ApiKey = apiKey };

    public async Task<AssistantReply> SendAsync(MessageCreateParams request, Action<string> onText,
                                                CancellationToken cancellationToken)
    {
        var aggregator = new BetaMessageContentAggregator();
        await foreach (var streamEvent in _client.Beta.Messages.CreateStreaming(request, cancellationToken)
                           .CollectAsync(aggregator).WithCancellation(cancellationToken))
        {
            if (streamEvent.TryPickContentBlockDelta(out var delta) && delta.Delta.TryPickText(out var text))
                onText(text.Text);
        }

        BetaMessage message = aggregator.Message();
        return ToReply(message);
    }

    private static AssistantReply ToReply(BetaMessage message)
    {
        var echo = new List<BetaContentBlockParam>();
        var calls = new List<ToolCall>();
        var text = new System.Text.StringBuilder();
        foreach (var block in message.Content)
        {
            if (block.TryPickText(out var t))
            {
                text.Append(t.Text);
                echo.Add(new BetaTextBlockParam { Text = t.Text });
            }
            else if (block.TryPickThinking(out var thinking))
            {
                echo.Add(new BetaThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
            }
            else if (block.TryPickRedactedThinking(out var redacted))
            {
                echo.Add(new BetaRedactedThinkingBlockParam { Data = redacted.Data });
            }
            else if (block.TryPickToolUse(out var use))
            {
                echo.Add(new BetaToolUseBlockParam { ID = use.ID, Name = use.Name, Input = use.Input });
                calls.Add(new ToolCall(use.ID, use.Name, use.Input));
            }
        }

        return new AssistantReply(text.ToString(), calls, message.StopReason?.Raw().ToString() ?? "", echo);
    }
}
