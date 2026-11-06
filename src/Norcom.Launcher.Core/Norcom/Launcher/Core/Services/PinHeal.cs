using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public sealed class HealHint
{
	public string Pin { get; init; } = "";

	public string Mc { get; init; } = "";

	public string Loader { get; init; } = "";

	public string MinLauncher { get; init; } = "";

	public string PackVersion { get; init; } = "";

	public string RepairAction { get; init; } = "resync-pack";
}

public static class PinHeal
{
	public const string HintUrl = "https://raw.githubusercontent.com/Jefftey97/norcom-pack/main/meta/heal-hint.json";

	public static int MaxAttempts(string? repairAction)
	{
		return string.Equals(repairAction, "resync-pack", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(repairAction) ? 2 : 1;
	}

	public static bool NeedsUpdate(string? localLauncher, string? localPack, HealHint? hint)
	{
		if (hint == null)
		{
			return false;
		}
		if (!string.IsNullOrWhiteSpace(hint.MinLauncher) && !string.IsNullOrWhiteSpace(localLauncher) && VersionCompare.Compare(hint.MinLauncher, localLauncher) > 0)
		{
			return true;
		}
		if (!string.IsNullOrWhiteSpace(hint.PackVersion) && !string.IsNullOrWhiteSpace(localPack) && VersionCompare.Compare(hint.PackVersion, localPack) > 0)
		{
			return true;
		}
		return false;
	}

	public static async Task<HealHint> FetchHintAsync(HttpClient http, CancellationToken ct)
	{
		HealHint hint = new HealHint();
		string? body = await TryGetAsync(http, HintUrl, ct).ConfigureAwait(false);
		if (!string.IsNullOrWhiteSpace(body))
		{
			hint = ParseHint(body);
		}
		string? compat = await TryGetAsync(http, PinGate.CompatPinUrl, ct).ConfigureAwait(false);
		if (!string.IsNullOrWhiteSpace(compat))
		{
			hint = MergeCompat(hint, compat);
		}
		if (string.IsNullOrWhiteSpace(hint.RepairAction))
		{
			return new HealHint
			{
				Pin = hint.Pin,
				Mc = hint.Mc,
				Loader = hint.Loader,
				MinLauncher = hint.MinLauncher,
				PackVersion = hint.PackVersion,
				RepairAction = "resync-pack"
			};
		}
		return hint;
	}

	public static async Task RunAsync(HttpClient http, string gameDir, TomlLite.Doc pack, string? packUrl, Func<CancellationToken, Task> resync, CancellationToken ct)
	{
		HealHint hint = await FetchHintAsync(http, ct).ConfigureAwait(false);
		pack.Keys.TryGetValue("version", out var packVersion);
		if (NeedsUpdate(NorcomVersion.Current, packVersion, hint))
		{
			LauncherLog.Write("pin feed behind minLauncher=" + hint.MinLauncher + " packVersion=" + hint.PackVersion);
		}
		RetargetJunction(gameDir);
		await NoteJavaAsync(http, ct).ConfigureAwait(false);
		int max = MaxAttempts(hint.RepairAction);
		for (int attempt = 1; attempt <= max; attempt++)
		{
		PinGate.PinCheck check = await PinGate.CheckAsync(http, gameDir, pack, ct, packUrl, null).ConfigureAwait(false);
		if (check == PinGate.PinCheck.Open)
			{
				await resync(ct).ConfigureAwait(false);
				return;
			}
			if (check == PinGate.PinCheck.Match)
			{
				if (attempt == 1)
				{
					await resync(ct).ConfigureAwait(false);
				}
				return;
			}
			LauncherLog.Write("pin heal attempt " + attempt + " " + hint.RepairAction);
			try
			{
				await resync(ct).ConfigureAwait(false);
			}
			catch (Exception ex) when (ex is not PinMismatchException)
			{
				LauncherLog.Write("pin heal failed " + ex.GetType().Name);
				if (attempt >= max)
				{
					throw new PinMismatchException(PinGate.BlockCopy);
				}
				continue;
			}
			check = await PinGate.CheckAsync(http, gameDir, pack, ct, packUrl, null).ConfigureAwait(false);
			if (check == PinGate.PinCheck.Match || check == PinGate.PinCheck.Open)
			{
				return;
			}
		}
		throw new PinMismatchException(PinGate.BlockCopy);
	}

	private static void RetargetJunction(string gameDir)
	{
		if (!PrismInstance.EndsWithNorcomGame(gameDir))
		{
			return;
		}
		string link = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrismLauncher", "instances", "NORCOM", "minecraft");
		try
		{
			PrismInstance.EnsureGameLink(link, gameDir);
			LauncherLog.Write("pin heal junction " + link);
		}
		catch (Exception ex)
		{
			LauncherLog.Write("pin heal junction " + ex.GetType().Name);
		}
	}

	private static async Task NoteJavaAsync(HttpClient http, CancellationToken ct)
	{
		string? java = Environment.GetEnvironmentVariable("NORCOM_JAVA");
		if (string.IsNullOrWhiteSpace(java) || File.Exists(java))
		{
			return;
		}
		LauncherLog.Write("pin heal java missing " + java);
		try
		{
			await JavaRuntime.EnsureAsync(http, new TransferMeter(), ct).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			LauncherLog.Write("pin heal java recheck " + ex.GetType().Name);
		}
	}

	private static HealHint ParseHint(string json)
	{
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement root = document.RootElement;
		return new HealHint
		{
			Pin = Text(root, "pin"),
			Mc = Text(root, "mc"),
			Loader = Text(root, "loader"),
			MinLauncher = Text(root, "minLauncher"),
			PackVersion = Text(root, "packVersion"),
			RepairAction = Text(root, "repairAction")
		};
	}

	private static HealHint MergeCompat(HealHint hint, string json)
	{
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement root = document.RootElement;
		return new HealHint
		{
			Pin = string.IsNullOrWhiteSpace(hint.Pin) ? Text(root, "pin") : hint.Pin,
			Mc = string.IsNullOrWhiteSpace(hint.Mc) ? Text(root, "mc") : hint.Mc,
			Loader = string.IsNullOrWhiteSpace(hint.Loader) ? Text(root, "loader") : hint.Loader,
			MinLauncher = string.IsNullOrWhiteSpace(hint.MinLauncher) ? Text(root, "minLauncher") : hint.MinLauncher,
			PackVersion = string.IsNullOrWhiteSpace(hint.PackVersion) ? Text(root, "packVersion") : hint.PackVersion,
			RepairAction = hint.RepairAction
		};
	}

	private static string Text(JsonElement element, string name)
	{
		if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return "";
		}
		return value.GetString()?.Trim() ?? "";
	}

	private static async Task<string?> TryGetAsync(HttpClient http, string url, CancellationToken ct)
	{
		try
		{
			using HttpResponseMessage response = await http.GetAsync(url, ct).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode)
			{
				return null;
			}
			return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
		{
			return null;
		}
	}
}
