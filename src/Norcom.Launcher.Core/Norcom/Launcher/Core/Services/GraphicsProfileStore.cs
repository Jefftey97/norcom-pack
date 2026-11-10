using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Norcom.Launcher.Core.Services;

public sealed class SavedGraphicsProfile
{
	public string Preset { get; set; } = "Low";

	public bool Auto { get; set; } = true;

	public string Reason { get; set; } = "";

	public Dictionary<string, string> Custom { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class GraphicsProfileStore
{
	public Dictionary<string, SavedGraphicsProfile> Profiles { get; } = new Dictionary<string, SavedGraphicsProfile>(StringComparer.OrdinalIgnoreCase);

	public static string FilePath(string launcherHome)
	{
		return Path.Combine(launcherHome, "graphics-profiles.json");
	}

	public SavedGraphicsProfile? Find(string gameId)
	{
		if (Profiles.TryGetValue(gameId, out SavedGraphicsProfile profile))
		{
			return profile;
		}
		return null;
	}

	public void Put(string gameId, SavedGraphicsProfile profile)
	{
		Profiles[gameId] = profile;
	}

	public static GraphicsProfileStore Load(string launcherHome)
	{
		GraphicsProfileStore store = new GraphicsProfileStore();
		string path = FilePath(launcherHome);
		if (!File.Exists(path))
		{
			return store;
		}
		try
		{
			using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
			if (!document.RootElement.TryGetProperty("profiles", out JsonElement profiles) || profiles.ValueKind != JsonValueKind.Object)
			{
				return store;
			}
			foreach (JsonProperty property in profiles.EnumerateObject())
			{
				SavedGraphicsProfile profile = property.Value.Deserialize<SavedGraphicsProfile>(JsonOptions()) ?? new SavedGraphicsProfile();
				if (profile.Custom == null)
				{
					profile.Custom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				}
				store.Profiles[property.Name] = profile;
			}
		}
		catch (Exception ex) when (ex is JsonException or IOException)
		{
			return store;
		}
		return store;
	}

	public void Save(string launcherHome)
	{
		Directory.CreateDirectory(launcherHome);
		Dictionary<string, SavedGraphicsProfile> profiles = new Dictionary<string, SavedGraphicsProfile>(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, SavedGraphicsProfile> pair in Profiles)
		{
			profiles[pair.Key] = pair.Value;
		}
		string json = JsonSerializer.Serialize(new Dictionary<string, object>
		{
			["profiles"] = profiles
		}, JsonOptions());
		File.WriteAllText(FilePath(launcherHome), json);
	}

	private static JsonSerializerOptions JsonOptions()
	{
		return new JsonSerializerOptions
		{
			WriteIndented = true,
			PropertyNameCaseInsensitive = true,
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase
		};
	}
}
