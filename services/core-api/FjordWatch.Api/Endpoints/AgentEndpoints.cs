using FjordWatch.Agent;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FjordWatch.Api.Endpoints;

public static class AgentEndpoints
{
    public const int MaxMessageLength = 2000;

    public static void MapAgentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/agent/chat", Chat).WithTags("agent");
    }

    public static async Task<Results<Ok<AgentResponse>, BadRequest<string>>> Chat(
        AgentRequest request,
        IAgent agent,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return TypedResults.BadRequest("message is required");
        }
        if (request.Message.Length > MaxMessageLength)
        {
            return TypedResults.BadRequest($"message exceeds {MaxMessageLength} characters");
        }
        var reply = await agent.AnswerAsync(request, ct);
        return TypedResults.Ok(reply);
    }
}
