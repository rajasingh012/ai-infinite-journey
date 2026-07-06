using AiInfiniteJourney.Shared;

namespace AiInfiniteJourney.Api.Services;

/// <summary>
/// The contract for any LLM-backed World Director. The game client talks to
/// ASP.NET, which talks to a concrete implementation. Swapping LLM providers
/// (MiniMax -> Gemma -> local Ollama) means swapping the DI registration, not
/// changing this interface or the controller.
/// </summary>
public interface IWorldDirector
{
    /// <summary>
    /// Send a freeform journey prompt and receive a structured JourneyPlan.
    /// </summary>
    Task<JourneyPlan> DirectAsync(string prompt, CancellationToken ct = default);
}
