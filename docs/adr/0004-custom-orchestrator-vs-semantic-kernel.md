# 0004. Custom one-shot orchestrator instead of Semantic Kernel for tool use

- **Status:** Accepted
- **Date:** 2026-05-03
- **Deciders:** Erdem Kilci

## Context

Phase 5 (LLM agent) needs an agent that can call five structured tools and a
RAG retriever, against either a local Ollama or Azure OpenAI. The phase plan
listed Semantic Kernel as the host because it is the natural .NET fit and a
good signal to senior reviewers. However, the
`Microsoft.SemanticKernel.Connectors.Ollama` package shipped at the time of
implementation requires a newer C# compiler (`4.14.0.0`) than the .NET 9.0.200
SDK provides (`4.13.0.0`); see CSC9057. Pinning around it would either pin
the whole SDK to an unreleased version or vendor a custom Ollama connector
that re-implements `IChatCompletionService`.

The remaining SK surface we would have used is the kernel's
`ToolCallBehavior.AutoInvokeKernelFunctions` loop and the
`KernelFunctionFromMethod` registration. Both are useful but neither is
load-bearing for our scope: one tool call per turn, deterministic dispatch,
and explicit citations.

## Decision

Implement a **small custom orchestrator** in `FjordWatch.Agent`:

- `IChatProvider` with `OllamaChatProvider` and `AzureOpenAIChatProvider` directly over HTTP (no Azure.AI.OpenAI SDK either, to keep the artifact lean).
- `IAgentTool` interface; tools are plain DI-registered services.
- `AgentOrchestrator.AnswerAsync` runs a one-shot dispatch: system prompt enumerates tools, model replies with a JSON tool call, orchestrator runs the tool, model writes the final prose answer using only the tool result.

This keeps the latency budget on Ollama predictable (one or two turns per
question), keeps citations first-class, and lets us swap providers with a
single env var.

## Considered alternatives

- **Semantic Kernel + a custom Ollama `IChatCompletionService`.** Pros: future-proof, idiomatic in .NET. Cons: writing the connector is more code than the orchestrator itself, and the auto-tool-call loop hides where citations come from.
- **Kernel Memory or LangChain via Python.** Pros: more mature RAG/tool ecosystems. Cons: introduces a second runtime in the agent path; loses the .NET typing story for tools.
- **Microsoft.SemanticKernel without the Ollama connector, calling Ollama via a custom provider.** Pros: keeps the SK abstraction. Cons: same as the first bullet, with the added cost of dual abstractions.

## Consequences

- **Positive:** small, auditable orchestrator (~120 LOC); citations are guaranteed by construction; no compiler-version coupling; one-env-var provider switch.
- **Negative:** no built-in support for multi-turn tool chains. We document one tool per turn as a feature, not a bug, and revisit if the eval suite needs longer plans.
- **Follow-ups:** if Microsoft ships a stable Ollama connector that matches our SDK, migrate the kernel construction so we get streaming, telemetry hooks, and the planner ecosystem for free.

## References

- Semantic Kernel Ollama connector: https://github.com/microsoft/semantic-kernel
- C# compiler version error CSC9057: https://learn.microsoft.com/dotnet/csharp/language-reference/compiler-messages/cs9057
