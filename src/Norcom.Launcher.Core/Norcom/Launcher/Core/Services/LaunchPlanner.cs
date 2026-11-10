using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Norcom.Launcher.Core.Services;

public static class LaunchPlanner
{
	public static LaunchPlan Build(string vanillaJson, string fabricJson, LaunchContext ctx)
	{
		LaunchContext ctx2 = ctx;
		List<LibraryFile> libraries;
		HashSet<string> seen;
		using (JsonDocument jsonDocument = JsonDocument.Parse(vanillaJson))
		{
			using JsonDocument jsonDocument2 = JsonDocument.Parse(fabricJson);
			JsonElement rootElement = jsonDocument.RootElement;
			JsonElement rootElement2 = jsonDocument2.RootElement;
			JsonElement value;
			string mainClass = (rootElement2.TryGetProperty("mainClass", out value) ? (value.GetString() ?? "") : (rootElement.GetProperty("mainClass").GetString() ?? ""));
			libraries = new List<LibraryFile>();
			seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			Take(rootElement, prefer: false);
			Take(rootElement2, prefer: true);
			int heap = Math.Max(512, ctx2.RamMb);
			string xms = "-Xms" + heap.ToString(System.Globalization.CultureInfo.InvariantCulture) + "M";
			string xmx = "-Xmx" + heap.ToString(System.Globalization.CultureInfo.InvariantCulture) + "M";
			List<string> list = new List<string>
			{
				xms,
				xmx
			};
			CollectArgs(rootElement, "jvm", ctx2, list);
			CollectArgs(rootElement2, "jvm", ctx2, list);
			list.RemoveAll((string a) => (a.StartsWith("-Xmx", StringComparison.Ordinal) && a != xmx) || (a.StartsWith("-Xms", StringComparison.Ordinal) && a != xms));
			if (!string.IsNullOrWhiteSpace(ctx2.LogConfigPath) && rootElement.TryGetProperty("logging", out var value2) && value2.TryGetProperty("client", out var value3) && value3.TryGetProperty("argument", out var value4))
			{
				string text = value4.GetString() ?? "";
				if (text.Length > 0)
				{
					list.Add(text.Replace("${path}", ctx2.LogConfigPath, StringComparison.Ordinal));
				}
			}
			List<string> list2 = new List<string>();
			CollectArgs(rootElement, "game", ctx2, list2);
			CollectArgs(rootElement2, "game", ctx2, list2);
			JsonElement value5;
			JsonElement value6;
			JsonElement value7;
			string text2 = ((rootElement.TryGetProperty("assetIndex", out value5) && value5.TryGetProperty("id", out value6)) ? (value6.GetString() ?? "") : (rootElement.TryGetProperty("assets", out value7) ? (value7.GetString() ?? "") : ""));
			for (int i = 0; i < list2.Count; i++)
			{
				list2[i] = list2[i].Replace("${assets_index_name}", text2, StringComparison.Ordinal);
			}
			return new LaunchPlan(mainClass, list, list2, libraries, text2);
		}
		void Take(JsonElement root, bool prefer)
		{
			if (!root.TryGetProperty("libraries", out var value8))
			{
				return;
			}
			foreach (JsonElement item in value8.EnumerateArray())
			{
				if (RulesAllow(item, ctx2))
				{
					JsonElement value9;
					string name = (item.TryGetProperty("name", out value9) ? (value9.GetString() ?? "") : "");
					if (LibraryCoordinates.TryResolve(item, out LibraryFile file))
					{
						if (!seen.Add(name))
						{
							if (!prefer)
							{
								continue;
							}
							libraries.RemoveAll((LibraryFile x) => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
						}
						libraries.Add(file);
					}
				}
			}
		}
	}

	public static bool RulesAllow(JsonElement owner, LaunchContext ctx)
	{
		if (!owner.TryGetProperty("rules", out var value) || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
		{
			return true;
		}
		bool? flag = null;
		foreach (JsonElement item in value.EnumerateArray())
		{
			if (item.TryGetProperty("features", out var _))
			{
				return false;
			}
			if (OsMatches(item, ctx))
			{
				JsonElement value3;
				string text = (item.TryGetProperty("action", out value3) ? value3.GetString() : "disallow");
				flag = text == "allow";
			}
		}
		return flag.GetValueOrDefault();
	}

	private static bool OsMatches(JsonElement rule, LaunchContext ctx)
	{
		if (!rule.TryGetProperty("os", out var value))
		{
			return true;
		}
		if (value.TryGetProperty("name", out var value2) && !value2.GetString().Equals(ctx.OsName, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (value.TryGetProperty("arch", out var value3))
		{
			string text = value3.GetString() ?? "";
			string text2 = (ctx.Arch.Equals("x64", StringComparison.OrdinalIgnoreCase) ? "x64" : ctx.Arch);
			if (text.Equals("x86", StringComparison.OrdinalIgnoreCase) && text2.Equals("x64", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			if (!text.Equals(text2, StringComparison.OrdinalIgnoreCase) && (!(text == "x86_64") || !(text2 == "x64")))
			{
				return false;
			}
		}
		return true;
	}

	private static void CollectArgs(JsonElement root, string side, LaunchContext ctx, List<string> into)
	{
		if (!root.TryGetProperty("arguments", out var value) || !value.TryGetProperty(side, out var value2))
		{
			return;
		}
		foreach (JsonElement item in value2.EnumerateArray())
		{
			if (item.ValueKind == JsonValueKind.String)
			{
				string @string = item.GetString();
				if (!string.IsNullOrEmpty(@string))
				{
					into.Add(Substitute(@string, ctx));
				}
			}
			else
			{
				if (item.ValueKind != JsonValueKind.Object || !ArgRulesAllow(item, ctx) || !item.TryGetProperty("value", out var value3))
				{
					continue;
				}
				if (value3.ValueKind == JsonValueKind.String)
				{
					into.Add(Substitute(value3.GetString() ?? "", ctx));
				}
				else
				{
					if (value3.ValueKind != JsonValueKind.Array)
					{
						continue;
					}
					foreach (JsonElement item2 in value3.EnumerateArray())
					{
						into.Add(Substitute(item2.GetString() ?? "", ctx));
					}
				}
			}
		}
	}

	private static bool ArgRulesAllow(JsonElement item, LaunchContext ctx)
	{
		if (!item.TryGetProperty("rules", out var value))
		{
			return true;
		}
		bool? flag = null;
		foreach (JsonElement item2 in value.EnumerateArray())
		{
			if (item2.TryGetProperty("features", out var value2) && value2.EnumerateObject().Any() && item2.TryGetProperty("action", out var value3) && value3.GetString() == "allow")
			{
				flag = false;
			}
			else if (OsMatches(item2, ctx))
			{
				JsonElement value4;
				string text = (item2.TryGetProperty("action", out value4) ? value4.GetString() : "disallow");
				flag = text == "allow";
			}
		}
		return flag.GetValueOrDefault();
	}

	public static string Substitute(string value, LaunchContext ctx)
	{
		return value.Replace("${auth_player_name}", ctx.PlayerName, StringComparison.Ordinal).Replace("${version_name}", ctx.VersionName, StringComparison.Ordinal).Replace("${game_directory}", ctx.GameDir, StringComparison.Ordinal)
			.Replace("${assets_root}", ctx.AssetsDir, StringComparison.Ordinal)
			.Replace("${game_assets}", ctx.AssetsDir, StringComparison.Ordinal)
			.Replace("${auth_uuid}", ctx.Uuid, StringComparison.Ordinal)
			.Replace("${auth_access_token}", ctx.AccessToken, StringComparison.Ordinal)
			.Replace("${auth_session}", ctx.AccessToken, StringComparison.Ordinal)
			.Replace("${user_type}", string.IsNullOrEmpty(ctx.UserType) ? "legacy" : ctx.UserType, StringComparison.Ordinal)
			.Replace("${version_type}", ctx.VersionType, StringComparison.Ordinal)
			.Replace("${clientid}", "", StringComparison.Ordinal)
			.Replace("${auth_xuid}", ctx.Xuid ?? "", StringComparison.Ordinal)
			.Replace("${launcher_name}", "NORCOM", StringComparison.Ordinal)
			.Replace("${launcher_version}", NorcomVersion.Current, StringComparison.Ordinal)
			.Replace("${classpath}", ctx.Classpath, StringComparison.Ordinal)
			.Replace("${natives_directory}", ctx.NativesDir, StringComparison.Ordinal)
			.Replace("${classpath_separator}", (OperatingSystem.IsWindows() || ctx.OsName == "windows") ? ";" : ":", StringComparison.Ordinal)
			.Replace("${library_directory}", "", StringComparison.Ordinal)
			.Replace("${user_properties}", "{}", StringComparison.Ordinal);
	}

	public static string SubstituteAssets(string value, string assetsIndex)
	{
		return value.Replace("${assets_index_name}", assetsIndex, StringComparison.Ordinal);
	}
}
