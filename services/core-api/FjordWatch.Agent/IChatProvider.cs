namespace FjordWatch.Agent;

/// <summary>
/// Minimal abstraction over a chat-completion model. Implementations:
/// <list type="bullet">
///   <item><see cref="Providers.OllamaChatProvider"/> talks to a local Ollama via /api/chat</item>
///   <item><see cref="Providers.AzureOpenAIChatProvider"/> talks to Azure OpenAI</item>
///   <item><see cref="Providers.FakeChatProvider"/> in the test project, returns scripted replies</item>
/// </list>
/// </summary>
public interface IChatProvider
{
    string Name { get; }
    string Model { get; }
    Task<string> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct);
}

public sealed record ChatMessage(ChatRole Role, string Content);

public enum ChatRole
{
    System,
    User,
    Assistant,
    Tool,
}
