using System;
using Microsoft.Extensions.AI;

namespace Spacer.AI.Tool;

/// <summary>
/// Lets an NPC say a line of dialogue. The model's call is buffered as a
/// <see cref="SpeakAction"/>; the line is delivered on the game thread through
/// the <c>say</c> sink the caller supplies (e.g. a dialogue bubble or chat log).
/// </summary>
public sealed class SpeakTool(Action<string> say) : IAgentTool
{
    public AITool Build(Agent agent) => AIFunctionFactory.Create(
        (string line) =>
        {
            agent.Enqueue(new SpeakAction(say, line));
            return "Spoken.";
        },
        name: "speak",
        description: "Say a line of dialogue aloud to nearby characters.");
}

/// <summary>Delivers one buffered line to the dialogue sink on the game thread.</summary>
public sealed class SpeakAction(Action<string> say, string line) : IAgentAction
{
    public void Apply() => say(line);
}
