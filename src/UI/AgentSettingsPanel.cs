using Godot;
using Spacer.AI;

namespace Spacer.UI;

/// <summary>
/// Settings UI for the LLM provider: pick a provider, fill in model / API key /
/// base URL, and apply. "Apply" hands a fresh <see cref="AgentSettings"/> to
/// <see cref="AgentManager.Reconfigure"/>, which persists it and hot-swaps the
/// live client.
///
/// The panel never mutates the manager's live settings; it edits a private
/// working copy so a half-finished edit can't affect running agents until Apply.
/// Fields the selected provider doesn't need (API key, base URL) are hidden.
/// </summary>
public partial class AgentSettingsPanel : PanelContainer
{
	private OptionButton _providerOption;
	private LineEdit _modelEdit;
	private LineEdit _apiKeyEdit;
	private LineEdit _baseUrlEdit;
	private CheckButton _revealKey;
	private Control _apiKeyRow;
	private Control _baseUrlRow;
	private Label _statusLabel;
	private Button _applyButton;

	// Working copy; only copied back to the manager on Apply.
	private AgentSettings _working;

	public override void _Ready()
	{
		_providerOption = GetNode<OptionButton>("%ProviderOption");
		_modelEdit = GetNode<LineEdit>("%ModelEdit");
		_apiKeyEdit = GetNode<LineEdit>("%ApiKeyEdit");
		_baseUrlEdit = GetNode<LineEdit>("%BaseUrlEdit");
		_revealKey = GetNode<CheckButton>("%RevealKey");
		_apiKeyRow = GetNode<Control>("%ApiKeyRow");
		_baseUrlRow = GetNode<Control>("%BaseUrlRow");
		_statusLabel = GetNode<Label>("%StatusLabel");
		_applyButton = GetNode<Button>("%ApplyButton");

		foreach (var p in AgentSettings.AgentProvider.All)
			_providerOption.AddItem(p.DisplayName, (int)p.Id);

		_working = CloneCurrentSettings();
		SelectProvider(_working.ProviderType);
		BindToUi();

		_providerOption.ItemSelected += OnProviderSelected;
		_modelEdit.TextChanged += text => _working.Model = text;
		_apiKeyEdit.TextChanged += text => _working.ApiKey = text;
		_baseUrlEdit.TextChanged += text => _working.BaseUrl = text;
		_revealKey.Toggled += pressed => _apiKeyEdit.Secret = !pressed;
		_applyButton.Pressed += OnApplyPressed;

		SetStatus("", false);
	}

	/// <summary>Snapshot the manager's live settings so edits stay local until Apply.</summary>
	private static AgentSettings CloneCurrentSettings()
	{
		var src = AgentManager.Instance?.Settings ?? AgentSettings.Load();
		return new AgentSettings
		{
			ProviderType = src.ProviderType,
			Model = src.Model,
			ApiKey = src.ApiKey,
			BaseUrl = src.BaseUrl,
		};
	}

	private void SelectProvider(AgentSettings.AgentProviderType type)
	{
		for (int i = 0; i < _providerOption.ItemCount; i++)
		{
			if (_providerOption.GetItemId(i) == (int)type)
			{
				_providerOption.Selected = i;
				return;
			}
		}
	}

	private void OnProviderSelected(long index)
	{
		_working.ProviderType = (AgentSettings.AgentProviderType)_providerOption.GetItemId((int)index);

		// Offer the new provider's defaults for any field the user hasn't filled.
		var info = _working.Info;
		if (string.IsNullOrWhiteSpace(_working.Model))
			_working.Model = info.DefaultModel;
		if (string.IsNullOrWhiteSpace(_working.BaseUrl))
			_working.BaseUrl = info.DefaultBaseUrl;

		BindToUi();
		SetStatus("", false);
	}

	/// <summary>Push the working copy into the widgets and toggle row visibility.</summary>
	private void BindToUi()
	{
		var info = _working.Info;

		_modelEdit.Text = _working.Model;
		_modelEdit.PlaceholderText = string.IsNullOrEmpty(info.DefaultModel) ? "model id" : info.DefaultModel;

		_apiKeyEdit.Text = _working.ApiKey;
		_baseUrlEdit.Text = _working.BaseUrl;
		_baseUrlEdit.PlaceholderText = string.IsNullOrEmpty(info.DefaultBaseUrl) ? "https://…" : info.DefaultBaseUrl;

		_apiKeyRow.Visible = info.RequiresApiKey;
		_baseUrlRow.Visible = info.RequiresBaseUrl;
	}

	private void OnApplyPressed()
	{
		if (!_working.TryValidate(out var error))
		{
			SetStatus(error, isError: true);
			return;
		}

		var manager = AgentManager.Instance;
		if (manager is null)
		{
			// No autoload (e.g. previewing the scene standalone) — just persist.
			_working.Save();
			SetStatus("Saved. (AgentManager not running; not applied.)", isError: false);
			return;
		}

		bool applied = manager.Reconfigure(_working);
		SetStatus(
			applied ? "Saved and applied." : "Saved, but the provider rejected the configuration — check the key/URL.",
			isError: !applied);

		// Continue editing the persisted copy rather than the manager's instance.
		_working = CloneCurrentSettings();
	}

	private void SetStatus(string message, bool isError)
	{
		_statusLabel.Text = message;
		_statusLabel.Visible = !string.IsNullOrEmpty(message);
		_statusLabel.Modulate = isError ? new Color(1f, 0.5f, 0.5f) : new Color(0.6f, 1f, 0.6f);
	}
}
