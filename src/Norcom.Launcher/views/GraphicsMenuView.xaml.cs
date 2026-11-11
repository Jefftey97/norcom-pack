using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Norcom.Launcher.Core;
using Norcom.Launcher.Core.Games;
using Norcom.Launcher.Core.Services;
using Norcom.Launcher.Core.Text;
using Norcom.Launcher.ViewModels;

namespace Norcom.Launcher.Views;

public partial class GraphicsMenuView : UserControl
{
	private string _gameId = "minecraft";

	private MachineSpecs _specs = new MachineSpecs();

	private PresetChoice _recommendation = new PresetChoice();

	private SavedGraphicsProfile _profile = new SavedGraphicsProfile();

	private bool _building;

	public static readonly DependencyProperty GameIdProperty = DependencyProperty.Register(nameof(GameId), typeof(string), typeof(GraphicsMenuView), new PropertyMetadata("minecraft", OnGameIdChanged));

	public string GameId
	{
		get
		{
			return (string)GetValue(GameIdProperty);
		}
		set
		{
			SetValue(GameIdProperty, value);
		}
	}

	public GraphicsMenuView()
	{
		InitializeComponent();
		Loaded += delegate
		{
			Refresh();
		};
		DataContextChanged += delegate
		{
			Refresh();
		};
	}

	private static void OnGameIdChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
	{
		GraphicsMenuView view = (GraphicsMenuView)sender;
		view._gameId = args.NewValue as string ?? "minecraft";
		if (view.IsLoaded)
		{
			view.Refresh();
		}
	}

	private void Refresh()
	{
		_gameId = string.IsNullOrWhiteSpace(GameId) ? "minecraft" : GameId;
		IGameBoard board = GameHost.Require(_gameId).Board;
		if (Heading != null)
		{
			Heading.Text = board.Heading;
		}
		if (TexturePanel != null)
		{
			TexturePanel.Visibility = board.ShowTexturePacks ? Visibility.Visible : Visibility.Collapsed;
		}
		_specs = MachineSpecsProbe.Read();
		SpecsText.Text = GameGraphicsCatalog.Describe(_specs);
		LoadProfile();
		BuildPresets();
		BuildManual();
		BuildMods();
		board.RefreshFeed(BuildMods);
	}

	private void LoadProfile()
	{
		(SavedGraphicsProfile saved, PresetChoice recommended) = GraphicsProfileStore.Resolve(NorcomPaths.NorcomHome, GameHost.Require(_gameId).Id, _specs);
		_recommendation = recommended;
		_profile = saved;
		RecommendationText.Text = string.IsNullOrEmpty(_profile.Preset)
			? "Recommended: " + _recommendation.Preset + ". " + _recommendation.Reason + " It is not applied until you click it."
			: "Using your " + _profile.Preset + " choice. Recommendation is " + _recommendation.Preset + ". " + _recommendation.Reason;
	}

	private void BuildPresets()
	{
		PresetButtons.Children.Clear();
		GameDefinition game = GameHost.Require(_gameId).Board.Definition;
		for (int i = 0; i < game.Presets.Length; i++)
		{
			string preset = game.Presets[i];
			Button button = new Button
			{
				Content = string.Equals(preset, _recommendation.Preset, StringComparison.OrdinalIgnoreCase) ? preset + " · recommended" : preset,
				Margin = new Thickness(0, 0, 10, 10),
				Tag = preset
			};
			button.Click += delegate(object sender, RoutedEventArgs args)
			{
				ChoosePreset((string)((Button)sender).Tag, auto: false);
			};
			PresetButtons.Children.Add(button);
		}
	}

	private void UseRecommendationClick(object sender, RoutedEventArgs args)
	{
		ChoosePreset(_recommendation.Preset, auto: true);
	}

	private void ResetPresetClick(object sender, RoutedEventArgs args)
	{
		_profile.Custom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		SaveAndApply();
		BuildManual();
	}

	private void ChoosePreset(string preset, bool auto)
	{
		_profile.Preset = preset;
		_profile.Auto = auto;
		_profile.Custom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		_profile.Reason = auto ? _recommendation.Reason : "Chosen in the graphics menu.";
		SaveAndApply();
		LoadProfile();
		BuildManual();
	}

	private void BuildManual()
	{
		_building = true;
		Manual.Children.Clear();
		GameDefinition game = GameHost.Require(_gameId).Board.Definition;
		Dictionary<string, string> values = GameGraphicsCatalog.EffectiveValues(game, _profile);
		for (int i = 0; i < game.Settings.Length; i++)
		{
			GraphicsSetting setting = game.Settings[i];
			values.TryGetValue(setting.Key, out string current);
			if (current == null)
			{
				current = "";
			}
			StackPanel block = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
			block.Children.Add(new TextBlock { Text = setting.Label, FontSize = 16 });
			block.Children.Add(new TextBlock { Text = setting.Help, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6) });
			block.Children.Add(Editor(setting, current));
			Manual.Children.Add(block);
		}
		_building = false;
	}

	private FrameworkElement Editor(GraphicsSetting setting, string current)
	{
		if (setting.Kind == "toggle")
		{
			CheckBox box = new CheckBox { Content = "On", IsChecked = current.Equals("true", StringComparison.OrdinalIgnoreCase), Tag = setting.Key };
			box.Checked += ToggleChanged;
			box.Unchecked += ToggleChanged;
			return box;
		}
		if (setting.Kind == "choice")
		{
			ComboBox combo = new ComboBox { MinWidth = 160, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left, Tag = setting.Key };
			for (int i = 0; i < setting.Choices.Length; i++)
			{
				combo.Items.Add(setting.Choices[i]);
			}
			combo.SelectedItem = current;
			combo.SelectionChanged += delegate
			{
				if (combo.SelectedItem is string picked)
				{
					SetCustom(setting.Key, picked);
				}
			};
			return combo;
		}
		StackPanel row = new StackPanel();
		Slider slider = new Slider
		{
			Minimum = setting.Min,
			Maximum = setting.Max,
			TickFrequency = setting.Step <= 0 ? 1 : setting.Step,
			IsSnapToTickEnabled = true,
			Width = 360,
			HorizontalAlignment = HorizontalAlignment.Left,
			Tag = setting.Key
		};
		if (double.TryParse(current, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
		{
			slider.Value = number;
		}
		TextBlock readout = new TextBlock { Text = current, Margin = new Thickness(0, 4, 0, 0) };
		slider.ValueChanged += delegate
		{
			string text = setting.Step >= 1 ? ((int)Math.Round(slider.Value)).ToString(CultureInfo.InvariantCulture) : slider.Value.ToString("0.##", CultureInfo.InvariantCulture);
			readout.Text = text;
			SetCustom(setting.Key, text);
		};
		row.Children.Add(slider);
		row.Children.Add(readout);
		return row;
	}

	private void ToggleChanged(object sender, RoutedEventArgs args)
	{
		CheckBox box = (CheckBox)sender;
		SetCustom((string)box.Tag, box.IsChecked == true ? "true" : "false");
	}

	private void SetCustom(string key, string value)
	{
		if (_building)
		{
			return;
		}
		if (_profile.Auto)
		{
			_profile.Auto = false;
			_profile.Custom = GameGraphicsCatalog.PresetValues(GameHost.Require(_gameId).Board.Definition, _profile.Preset);
			_profile.Reason = "Custom settings for this game.";
		}
		if (_profile.Custom == null)
		{
			_profile.Custom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		}
		_profile.Custom[key] = value;
		SaveAndApply();
		RecommendationText.Text = "Using your " + _profile.Preset + " override. Recommendation was " + _recommendation.Preset + ". " + _recommendation.Reason;
	}

	private void SaveAndApply()
	{
		GraphicsProfileStore store = GraphicsProfileStore.Load(NorcomPaths.NorcomHome);
		IGameBoard board = GameHost.Require(_gameId).Board;
		store.Put(board.Id, _profile);
		store.Save(NorcomPaths.NorcomHome);
		if (GameSession.IsRunning(board.Id, NorcomPaths.GameLockPath))
		{
			StatusText.Text = "Saved. The game is running, so the config file was not changed.";
			return;
		}
		string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		StatusText.Text = board.Apply(_profile, NorcomPaths.GameDir, user);
	}

	private void BuildMods()
	{
		Mods.Children.Clear();
		IGameBoard board = GameHost.Require(_gameId).Board;
		string config = board.ConfigDirectory(NorcomPaths.GameDir, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
		string schemaPath = Path.Combine(config, "norcom-settings-schema.json");
		string schema = File.Exists(schemaPath) ? File.ReadAllText(schemaPath) : "";
		IReadOnlyList<DiscoveredModFile> files = board.DiscoverMods(config, schema);
		if (files.Count == 0)
		{
			Mods.Children.Add(new TextBlock { Text = "No mods in the pack yet.", Opacity = 0.75 });
			return;
		}
		for (int i = 0; i < files.Count; i++)
		{
			DiscoveredModFile file = files[i];
			StackPanel block = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
			string title = file.RelativePath + (file.Preserved ? " · pack will not replace this file" : "") + (file.ReadOnly ? " · read-only" : "");
			block.Children.Add(new TextBlock { Text = title, FontSize = 14 });
			if (!string.IsNullOrWhiteSpace(file.Note))
			{
				block.Children.Add(new TextBlock { Text = file.Note, Opacity = 0.75, TextWrapping = TextWrapping.Wrap });
			}
			if (!file.ReadOnly)
			{
				Dictionary<string, TextBox> boxes = new Dictionary<string, TextBox>(StringComparer.OrdinalIgnoreCase);
				for (int s = 0; s < file.Settings.Count; s++)
				{
					DiscoveredSetting setting = file.Settings[s];
					block.Children.Add(new TextBlock { Text = setting.Label, Margin = new Thickness(0, 6, 0, 0) });
					if (!string.IsNullOrWhiteSpace(setting.Help))
					{
						block.Children.Add(new TextBlock { Text = setting.Help, Opacity = 0.75, TextWrapping = TextWrapping.Wrap });
					}
					if (setting.ReadOnly)
					{
						block.Children.Add(new TextBlock { Text = setting.Value, Opacity = 0.75 });
						continue;
					}
					TextBox box = new TextBox { Text = setting.Value, Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
					boxes[setting.Key] = box;
					block.Children.Add(box);
				}
				Button save = new Button { Content = "Save " + file.RelativePath, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
				save.Click += delegate
				{
					Dictionary<string, string> edits = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
					foreach (KeyValuePair<string, TextBox> pair in boxes)
					{
						edits[pair.Key] = pair.Value.Text ?? "";
					}
					if (!ModSettingsCatalog.TrySave(config, file, edits, out string error))
					{
						StatusText.Text = error;
						return;
					}
					StatusText.Text = "Saved " + file.RelativePath + ".";
				};
				block.Children.Add(save);
			}
			Mods.Children.Add(block);
		}
	}

	private static string PreserveIndex()
	{
		string cached = Path.Combine(NorcomPaths.PackwizCacheDir, "index.toml");
		if (File.Exists(cached))
		{
			return File.ReadAllText(cached);
		}
		if (!File.Exists(NorcomPaths.ManagedManifestPath))
		{
			return "";
		}
		try
		{
			List<ManagedEntry> entries = JsonSerializer.Deserialize<List<ManagedEntry>>(File.ReadAllText(NorcomPaths.ManagedManifestPath));
			if (entries == null)
			{
				return "";
			}
			StringBuilder builder = new StringBuilder();
			for (int i = 0; i < entries.Count; i++)
			{
				if (!entries[i].Preserve)
				{
					continue;
				}
				builder.Append("[[files]]\nfile = \"").Append(entries[i].RelativePath).Append("\"\npreserve = true\n");
			}
			return builder.ToString();
		}
		catch (JsonException)
		{
			return "";
		}
	}
}
