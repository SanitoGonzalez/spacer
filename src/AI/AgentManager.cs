using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Microsoft.Extensions.AI;

namespace Spacer.AI;

/// <summary>
/// Autoload singleton that owns the shared LLM client and drives NPC
/// <see cref="Agent"/>s. An agent is an LLM-backed loop: it receives an
/// observation about the world, reasons, and acts by calling tools (game
/// actions). Tool calls are executed automatically by the function-invocation
/// middleware and fed back to the model until it produces a final reply.
///
/// Registered as the autoload "AgentManager"; access from anywhere via
/// <see cref="Instance"/>. Provider/model/key come from <see cref="AgentSettings"/>;
/// the settings UI calls <see cref="Reconfigure"/> to hot-swap the client.
/// </summary>
public partial class AgentManager : Node
{
	public static AgentManager Instance { get; private set; } = null!;
	public AgentSettings Settings { get; private set; } = null!;

	private readonly List<Agent> _agents = [];

	/// <summary>Current configuration (provider, model, key, base URL).</summary>

	/// <summary>The active client, or null until the AI is configured.</summary>
	public IChatClient Client { get; private set; } = null!;

	public override void _EnterTree()
	{
		Instance = this;
	}

	public override void _Ready()
	{
		Settings = AgentSettings.Load();
		TryInitClient();
	}

	public override void _ExitTree()
	{
		Client.Dispose();
	}

	/// <summary>
	/// Drain every agent's buffered actions on the main thread. Tool delegates run
	/// off-thread and only enqueue; this is where their effects actually hit the
	/// scene tree, in the order the model called them.
	/// </summary>
	public override void _Process(double delta)
	{
		foreach (var agent in _agents)
			agent.ApplyActions();
	}

	/// <summary>
	/// Persist new settings and rebuild the client. Returns false (and warns) if
	/// the config is incomplete or the provider rejects it; agents go inert until
	/// a valid config is supplied.
	/// </summary>
	public bool Reconfigure(AgentSettings settings)
	{
		Settings = settings;
		Settings.Save();
		return TryInitClient();
	}

	private bool TryInitClient()
	{
		Client.Dispose();

		if (!Settings.TryValidate(out var error))
		{
			GD.PushWarning($"{nameof(AgentManager)}: AI not configured — {error} Agents are inert until configured.");
			return false;
		}

		try
		{
			Client = Settings.Create();
			return true;
		}
		catch (Exception e)
		{
			GD.PushError($"{nameof(AgentManager)}: failed to create chat client — {e.Message}");
			return false;
		}
	}

	/// <summary>
	/// Create an NPC agent with a persona and the actions it may take.
	/// </summary>
	public Agent CreateAgent(string persona, IReadOnlyList<IAgentTool> tools)
	{
		var agent = new Agent(persona, tools);
		_agents.Add(agent);
		return agent;
	}

	/// <summary>
	/// One agentic step for every active NPC. Call from a timer or game tick —
	/// NOT from _Process every frame (each step is a network round-trip). Agents
	/// already busy with a prior step skip this tick. Buffered actions land on the
	/// main thread via <see cref="_Process"/>, not here.
	/// </summary>
	public async Task TickAsync(CancellationToken ct = default)
	{
		await Task.WhenAll(_agents.Select(a => a.StepAsync(ct)));
	}
}
