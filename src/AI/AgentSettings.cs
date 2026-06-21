using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Linq;
using Anthropic.SDK;
using Godot;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;

namespace Spacer.AI;

/// <summary>
/// The chosen provider, model, API key and base URL.
///
/// Loaded from <c>user://ai.cfg</c>, then overlaid with environment variables so
/// a developer can point the game at a provider without touching the settings UI:
///   SPACER_AI_PROVIDER, SPACER_AI_MODEL, SPACER_AI_BASE_URL, SPACER_AI_API_KEY
/// plus the provider's conventional key var (e.g. ANTHROPIC_API_KEY).
///
/// Note: <c>user://</c> is plaintext on the player's machine. That's acceptable
/// for bring-your-own-key; do NOT ship a project with a real key baked in.
/// </summary>
public sealed class AgentSettings
{
	public enum AgentProviderType
	{
		Anthropic,
		OpenAi,
		Ollama,
		OpenAiCompatible,
	}
	
	/// <summary>
	/// Static description of a provider: what the user must supply and sensible
	/// defaults. Adding a provider = add an <see cref="AgentProviderType"/> value, a row
	/// in <see cref="All"/>, and a switch arm in <see cref="AgentSettings.Create"/>.
	/// </summary>
	public sealed record AgentProvider(
		AgentProviderType Id,
		string DisplayName,
		bool RequiresApiKey,
		bool RequiresBaseUrl,
		string DefaultModel,
		string DefaultBaseUrl,
		string ApiKeyEnvVar)
	{
		public static readonly IReadOnlyList<AgentProvider> All =
		[
			// NPCs run many turns — defaults favour small/fast models.
			new AgentProvider(AgentProviderType.Anthropic, "Anthropic (Claude)",
				RequiresApiKey: true,  RequiresBaseUrl: false,
				DefaultModel: "claude-haiku-4-5", DefaultBaseUrl: "", ApiKeyEnvVar: "ANTHROPIC_API_KEY"),

			new AgentProvider(AgentProviderType.OpenAi, "OpenAI",
				RequiresApiKey: true,  RequiresBaseUrl: false,
				DefaultModel: "gpt-4o-mini", DefaultBaseUrl: "", ApiKeyEnvVar: "OPENAI_API_KEY"),

			new AgentProvider(AgentProviderType.Ollama, "Ollama (local)",
				RequiresApiKey: false, RequiresBaseUrl: true,
				DefaultModel: "llama3.2", DefaultBaseUrl: "http://localhost:11434", ApiKeyEnvVar: ""),

			new AgentProvider(AgentProviderType.OpenAiCompatible, "OpenAI-compatible",
				RequiresApiKey: true,  RequiresBaseUrl: true,
				DefaultModel: "", DefaultBaseUrl: "", ApiKeyEnvVar: "OPENAI_API_KEY")
		];

		public static AgentProvider Get(AgentProviderType id) => All.First(p => p.Id == id);
	}

	private const string ConfigPath = "user://ai.cfg";
	private const string Section = "ai";

	public AgentProviderType ProviderType { get; set; } = AgentProviderType.Anthropic;
	public string Model { get; set; } = "";
	public string ApiKey { get; set; } = "";
	public string BaseUrl { get; set; } = "";

	public AgentProvider Info => AgentProvider.Get(ProviderType);

	public static AgentSettings Load()
	{
		var s = new AgentSettings();

		var cfg = new ConfigFile();
		if (cfg.Load(ConfigPath) == Error.Ok)
		{
			s.ProviderType = (AgentProviderType)(int)cfg.GetValue(Section, "provider", (int)AgentProviderType.Anthropic);
			s.Model = (string)cfg.GetValue(Section, "model", "");
			s.ApiKey = (string)cfg.GetValue(Section, "api_key", "");
			s.BaseUrl = (string)cfg.GetValue(Section, "base_url", "");
		}

		s.ApplyEnvOverrides();
		s.ApplyDefaults();
		return s;
	}

	public void Save()
	{
		var cfg = new ConfigFile();
		cfg.SetValue(Section, "provider", (int)ProviderType);
		cfg.SetValue(Section, "model", Model);
		cfg.SetValue(Section, "api_key", ApiKey);
		cfg.SetValue(Section, "base_url", BaseUrl);
		cfg.Save(ConfigPath);
	}

	/// <summary>True if everything the selected provider needs is present.</summary>
	public bool TryValidate(out string error)
	{
		if (Info.RequiresApiKey && string.IsNullOrWhiteSpace(ApiKey))
		{
			error = $"{Info.DisplayName} requires an API key.";
			return false;
		}
		if (Info.RequiresBaseUrl && string.IsNullOrWhiteSpace(BaseUrl))
		{
			error = $"{Info.DisplayName} requires a base URL.";
			return false;
		}
		if (string.IsNullOrWhiteSpace(Model))
		{
			error = "A model id is required.";
			return false;
		}
		error = "";
		return true;
	}

	/// <summary>
	/// Build a function-invoking <see cref="IChatClient"/> for these settings.
	/// The only place provider SDKs are referenced.
	/// </summary>
	public IChatClient Create()
	{
		IChatClient inner = ProviderType switch
		{
			AgentProviderType.Anthropic =>
				new AnthropicClient(ApiKey).Messages,

			AgentProviderType.OpenAi =>
				new OpenAIClient(new ApiKeyCredential(ApiKey))
					.GetChatClient(Model)
					.AsIChatClient(),

			AgentProviderType.OpenAiCompatible =>
				new OpenAIClient(
						new ApiKeyCredential(string.IsNullOrEmpty(ApiKey) ? "unused" : ApiKey),
						new OpenAIClientOptions { Endpoint = new Uri(BaseUrl) })
					.GetChatClient(Model)
					.AsIChatClient(),

			AgentProviderType.Ollama =>
				new OllamaApiClient(new Uri(BaseUrl)) { SelectedModel = Model },

			_ => throw new ArgumentOutOfRangeException(nameof(ProviderType)),
		};

		// Auto-execute tool calls the model emits and loop results back.
		return inner
			.AsBuilder()
			.UseFunctionInvocation()
			.Build();
	}

	private void ApplyEnvOverrides()
	{
		var p = OS.GetEnvironment("SPACER_AI_PROVIDER");
		if (!string.IsNullOrEmpty(p) && Enum.TryParse<AgentProviderType>(p, ignoreCase: true, out var parsed))
			ProviderType = parsed;

		var model = OS.GetEnvironment("SPACER_AI_MODEL");
		if (!string.IsNullOrEmpty(model)) Model = model;

		var baseUrl = OS.GetEnvironment("SPACER_AI_BASE_URL");
		if (!string.IsNullOrEmpty(baseUrl)) BaseUrl = baseUrl;

		// Generic override wins; otherwise fall back to the provider's standard var.
		var key = OS.GetEnvironment("SPACER_AI_API_KEY");
		if (string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(Info.ApiKeyEnvVar))
			key = OS.GetEnvironment(Info.ApiKeyEnvVar);
		if (!string.IsNullOrEmpty(key)) ApiKey = key;
	}

	private void ApplyDefaults()
	{
		if (string.IsNullOrEmpty(Model)) Model = Info.DefaultModel;
		if (string.IsNullOrEmpty(BaseUrl)) BaseUrl = Info.DefaultBaseUrl;
	}
}
