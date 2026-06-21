namespace Spacer.AI;

/// <summary>
/// A buffered effect on the game world, produced when the model calls an action
/// tool. Tools run off the main thread, so the mutation is deferred: the tool
/// enqueues the action and the game loop drains the buffer, calling
/// <see cref="Apply"/> on the main thread where touching the scene tree is safe.
/// One model step may enqueue many actions; they are applied in call order.
/// </summary>
public interface IAgentAction
{
    void Apply();
}
