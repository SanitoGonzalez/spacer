using System;
using System.Collections.Generic;
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
	/// <summary>The autoloaded instance.</summary>
	public static AgentManager Instance { get; private set; }

	private readonly List<Agent> _agents = [];

	/// <summary>Current configuration (provider, model, key, base URL).</summary>
	public AgentSettings Settings { get; private set; }

	/// <summary>The active client, or null until the AI is configured.</summary>
	public IChatClient Client { get; private set; }

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
		Client?.Dispose();
		if (Instance == this)
			Instance = null;
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
		Client?.Dispose();
		Client = null;

		if (!Settings.TryValidate(out var error))
		{
			GD.PushWarning($"AgentManager: AI not configured — {error} Agents are inert until configured.");
			return false;
		}

		try
		{
			Client = Settings.Create();
			return true;
		}
		catch (Exception e)
		{
			GD.PushError($"AgentManager: failed to create chat client — {e.Message}");
			return false;
		}
	}

	/// <summary>
	/// Create an NPC agent with a persona and the actions it may take.
	/// </summary>
	public Agent CreateAgent(string name, string persona, IList<AITool> tools)
	{
		var agent = new Agent(this, name, persona, tools);
		_agents.Add(agent);
		return agent;
	}

	/// <summary>
	/// Example of defining a game action as a tool. Continuations after an await
	/// run on Godot's main thread (Godot installs a SynchronizationContext), so
	/// it is safe to touch the scene tree inside these.
	/// </summary>
	public static IList<AITool> BuildExampleTools(/* e.g. CharacterBody3D body */)
	{
		return new List<AITool>
		{
			AIFunctionFactory.Create(
				(string direction) => $"Moved {direction}.",
				name: "move",
				description: "Move the NPC in a direction: north, south, east, or west."),

			AIFunctionFactory.Create(
				() => "You see an empty corridor.",
				name: "look",
				description: "Observe the NPC's immediate surroundings."),
		};
	}

	/// <summary>
	/// One agentic step for every active NPC. Call from a timer or game tick —
	/// NOT from _Process every frame (each step is a network round-trip).
	/// </summary>
	public async Task TickAsync(CancellationToken ct = default)
	{
		if (Client is null)
			return;

		foreach (var agent in _agents)
		{
			// TODO: build a real observation for this NPC from game state.
			string reply = await agent.StepAsync("Describe what you do next.", ct);
			GD.Print($"[{agent.Name}] {reply}");
		}
	}
}
