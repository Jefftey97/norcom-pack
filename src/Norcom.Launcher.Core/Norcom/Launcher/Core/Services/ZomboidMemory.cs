using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Norcom.Launcher.Core.Services;

public static class ZomboidMemory
{
	public const string FileName = "ProjectZomboid64.json";

	public const string BackupName = "ProjectZomboid64.json.norcom-backup";

	public static string Apply(string userZomboidDir, string installDir, int ramMb)
	{
		if (ramMb < 512)
		{
			return "";
		}
		Directory.CreateDirectory(userZomboidDir);
		string dest = Path.Combine(userZomboidDir, FileName);
		string backup = Path.Combine(userZomboidDir, BackupName);
		string install = string.IsNullOrEmpty(installDir) ? "" : Path.Combine(installDir, FileName);
		JsonObject root;
		if (File.Exists(dest))
		{
			if (!File.Exists(backup))
			{
				File.Copy(dest, backup, overwrite: false);
			}
			root = Parse(File.ReadAllText(dest)) ?? new JsonObject();
		}
		else
		{
			string seed = !string.IsNullOrEmpty(install) && File.Exists(install) ? File.ReadAllText(install) : "";
			if (seed.Length > 0 && !File.Exists(backup))
			{
				File.WriteAllText(backup, seed);
			}
			root = seed.Length == 0 ? new JsonObject() : Parse(seed) ?? new JsonObject();
		}
		SetHeap(root, ramMb);
		if (root["windows"] is JsonObject windows)
		{
			SetHeap(windows, ramMb);
		}
		File.WriteAllText(dest, root.ToJsonString(new JsonSerializerOptions
		{
			WriteIndented = true
		}));
		return dest;
	}

	public static string WithHeap(string url, int ramMb)
	{
		if (ramMb < 512 || string.IsNullOrEmpty(url))
		{
			return url ?? "";
		}
		string arg = "-Xmx" + ramMb.ToString(CultureInfo.InvariantCulture) + "m";
		return url.Contains("//", StringComparison.Ordinal) ? url + "%20" + arg : url + "//" + arg;
	}

	private static void SetHeap(JsonObject obj, int ramMb)
	{
		JsonArray array = obj["vmArgs"] as JsonArray ?? new JsonArray();
		string xms = "-Xms" + ramMb.ToString(CultureInfo.InvariantCulture) + "m";
		string xmx = "-Xmx" + ramMb.ToString(CultureInfo.InvariantCulture) + "m";
		bool sawMs = false;
		bool sawMx = false;
		for (int i = 0; i < array.Count; i++)
		{
			if (array[i] is not JsonValue value || !value.TryGetValue<string>(out string text) || text == null)
			{
				continue;
			}
			if (text.StartsWith("-Xms", StringComparison.OrdinalIgnoreCase))
			{
				array[i] = xms;
				sawMs = true;
			}
			else if (text.StartsWith("-Xmx", StringComparison.OrdinalIgnoreCase))
			{
				array[i] = xmx;
				sawMx = true;
			}
		}
		if (!sawMs)
		{
			array.Add(xms);
		}
		if (!sawMx)
		{
			array.Add(xmx);
		}
		obj["vmArgs"] = array;
	}

	private static JsonObject? Parse(string text)
	{
		try
		{
			return JsonNode.Parse(text) as JsonObject;
		}
		catch (JsonException)
		{
			return null;
		}
	}
}
