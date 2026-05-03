using FjordWatch.Agent;
using FjordWatch.Agent.Tools;
using FluentAssertions;

namespace FjordWatch.Api.Tests.Agent;

public class AgentOrchestratorTests
{
    [Fact]
    public void TryParseToolCall_extracts_known_tool()
    {
        var ok = AgentOrchestrator.TryParseToolCall(
            "  {\"tool\": \"nearest_vessels\", \"args\": {\"lat\": 60.5, \"lon\": 5.0}}  ",
            out var name, out var args);
        ok.Should().BeTrue();
        name.Should().Be("nearest_vessels");
        args.Should().ContainKey("lat").WhoseValue.Should().Be("60.5");
        args.Should().ContainKey("lon").WhoseValue.Should().Be("5.0");
    }

    [Fact]
    public void TryParseToolCall_rejects_non_object_replies()
    {
        var ok = AgentOrchestrator.TryParseToolCall("just plain prose", out var _, out var _);
        ok.Should().BeFalse();
    }

    [Fact]
    public async Task AnswerAsync_runs_tool_and_returns_citation()
    {
        var tool = new RecordingTool();
        var provider = new ScriptedChatProvider(
            "{\"tool\": \"recording\", \"args\": {\"x\": 1}}",
            "Final answer with one citation.");
        var orchestrator = new AgentOrchestrator(provider, [tool]);

        var resp = await orchestrator.AnswerAsync(new AgentRequest("hello", null), CancellationToken.None);

        resp.Reply.Should().Be("Final answer with one citation.");
        resp.Citations.Should().HaveCount(1);
        resp.Citations[0].Source.Should().Be("recording");
        tool.InvocationCount.Should().Be(1);
    }

    [Fact]
    public async Task AnswerAsync_returns_first_reply_when_no_tool_call()
    {
        var tool = new RecordingTool();
        var provider = new ScriptedChatProvider("There are no live anomalies right now.");
        var orchestrator = new AgentOrchestrator(provider, [tool]);

        var resp = await orchestrator.AnswerAsync(new AgentRequest("status?", null), CancellationToken.None);

        resp.Reply.Should().Be("There are no live anomalies right now.");
        resp.Citations.Should().BeEmpty();
        tool.InvocationCount.Should().Be(0);
    }

    private sealed class RecordingTool : IAgentTool
    {
        public int InvocationCount { get; private set; }
        public string Name => "recording";
        public string Description => "test tool";
        public string ParameterSchema => "{ x: number }";

        public Task<ToolResult> InvokeAsync(IReadOnlyDictionary<string, string> args, CancellationToken ct)
        {
            InvocationCount++;
            return Task.FromResult(new ToolResult(
                "ok",
                new Citation(Name, "1 row", args.ToDictionary(kv => kv.Key, kv => kv.Value))));
        }
    }

    private sealed class ScriptedChatProvider : IChatProvider
    {
        private readonly Queue<string> _replies;

        public ScriptedChatProvider(params string[] replies)
        {
            _replies = new Queue<string>(replies);
        }

        public string Name => "scripted";
        public string Model => "scripted";

        public Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct)
        {
            return Task.FromResult(_replies.Count > 0 ? _replies.Dequeue() : "");
        }
    }
}
