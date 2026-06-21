using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Spacer.AI;

/// <summary>
/// An LLM-backed NPC: it is fed an observation, reasons, and acts by calling
/// tools. Tool calls are auto-executed by the function-invocation middleware
/// during <see cref="StepAsync"/>, which runs off the main thread — so action
/// tools don't mutate the world directly. They enqueue <see cref="IAgentAction"/>s
/// into a thread-safe buffer that the game loop drains via <see cref="ApplyActions"/>
/// on the main thread. One step may enqueue many actions.
/// </summary>
public sealed class Agent
{
    private readonly ChatOptions _options;
    private readonly List<ChatMessage> _history = [];
    private readonly ConcurrentQueue<IAgentAction> _pending = new();

    // Guard so a step that outlasts a tick isn't started again concurrently.
    private int _stepping;

    internal Agent(string persona, IReadOnlyList<IAgentTool> tools)
    {
        _history.Add(new ChatMessage(ChatRole.System, persona));
        _options = new ChatOptions { Tools = tools.Select(tool => tool.Build(this)).ToList() };
    }

    /// <summary>
    /// Buffer an action for the game loop to apply. Called from tool delegates,
    /// which run off the main thread; the queue is the thread-safe handoff.
    /// </summary>
    internal void Enqueue(IAgentAction action) => _pending.Enqueue(action);

    /// <summary>
    /// Advance one turn: feed an observation, let the model reason and call tools
    /// (which buffer actions), and persist the exchange to history. Returns the
    /// model's final text, or empty string if a previous step is still running.
    /// </summary>
    public async Task<string> StepAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _stepping, 1) == 1)
            return string.Empty;

        try
        {
            var manager = AgentManager.Instance;
            _options.ModelId = manager.Settings.Model;
            _history.Add(new ChatMessage(ChatRole.User, "Decide your next action."));

            ChatResponse response = await manager.Client.GetResponseAsync(_history, _options, ct);
            _history.AddMessages(response);

            return response.Text;
        }
        finally
        {
            Interlocked.Exchange(ref _stepping, 0);
        }
    }

    /// <summary>
    /// Apply and clear all buffered actions. MUST be called on the game/main
    /// thread (e.g. from <c>_Process</c>), since actions touch the scene tree.
    /// </summary>
    public void ApplyActions()
    {
        while (_pending.TryDequeue(out var action))
            action.Apply();
    }

    /// <summary>Forget the conversation (keep the persona) and drop pending actions.</summary>
    public void Reset()
    {
        var persona = _history[0];
        _history.Clear();
        _history.Add(persona);

        while (_pending.TryDequeue(out _)) { }
    }
}
