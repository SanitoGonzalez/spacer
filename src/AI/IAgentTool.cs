using Microsoft.Extensions.AI;

namespace Spacer.AI;

/// <summary>
/// A capability the model may call. Instances are bound to whatever they act on
/// (a specific NPC, body, dialogue sink, ...), so a tool can carry per-agent
/// state — unlike a static definition.
///
/// A tool's delegate runs on a thread-pool thread (the function-invocation
/// middleware continues off the main thread), so it must NOT touch the scene
/// tree. Instead it either returns data read from a pre-step snapshot (a query)
/// or enqueues an <see cref="IAgentAction"/> via <see cref="Agent.Enqueue"/> and
/// returns an acknowledgement (an action). Buffered actions are applied later on
/// the game thread.
/// </summary>
public interface IAgentTool
{
    AITool Build(Agent agent);
}
