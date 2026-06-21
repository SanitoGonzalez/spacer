using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace Spacer.AI;

/// <summary>
/// A single NPC: persona + conversation history + the tools it can call.
/// Reads the live client from its <see cref="AgentManager"/>, so a
/// <see cref="AgentManager.Reconfigure"/> mid-game takes effect on the next step.
/// Create instances via <see cref="AgentManager.CreateAgent"/>.
/// </summary>
public sealed class Agent
{
	private readonly AgentManager _manager;
	private readonly List<ChatMessage> _history = [];
	private readonly ChatOptions _options;

	public string Name { get; }

	internal Agent(AgentManager manager, string name, string persona, IList<AITool> tools)
	{
		_manager = manager;
		Name = name;
		_history.Add(new ChatMessage(ChatRole.System, persona));
		_options = new ChatOptions { Tools = tools };
	}

	/// <summary>
	/// Advance one turn: feed an observation, let the model reason and call
	/// tools (executed by the function-invocation middleware), and return its
	/// final text. Tool calls and results are persisted to history.
	/// </summary>
	public async Task<string> StepAsync(string observation, CancellationToken ct = default)
	{
		var client = _manager.Client
			?? throw new InvalidOperationException("AI is not configured; see the settings UI.");

		_options.ModelId = _manager.Settings.Model;
		_history.Add(new ChatMessage(ChatRole.User, observation));

		ChatResponse response = await client.GetResponseAsync(_history, _options, ct);

		// Append the assistant + any tool turns so the next step has context.
		_history.AddRange(response.Messages);
		return response.Text;
	}

	/// <summary>Drop history back to the persona, e.g. on respawn.</summary>
	public void Reset()
	{
		var persona = _history[0];
		_history.Clear();
		_history.Add(persona);
	}
}
