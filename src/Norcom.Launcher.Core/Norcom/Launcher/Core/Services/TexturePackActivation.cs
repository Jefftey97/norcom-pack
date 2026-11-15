using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Norcom.Launcher.Core.Models;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public static class TexturePackActivation
{
	public static async Task EnableAsync(string gameDir, string choice, HttpClient http, TransferMeter meter, CancellationToken ct)
	{
		byte[] before = SnapshotOptions(gameDir);
		try
		{
			if (string.Equals(TexturePackChoice.Normalize(choice), TexturePackChoice.Alacrity, StringComparison.Ordinal))
			{
				await KnownVisualAssets.EnsureAsync(http, gameDir, meter, ct, includeAlacrity: true, alacrityOnly: true).ConfigureAwait(false);
			}
			TexturePackChoice.ApplyPicker(gameDir, choice);
		}
		catch
		{
			RestoreOptions(gameDir, before);
			throw;
		}
	}

	public static Task EnsureSelectedAsync(string gameDir, string savedChoice, bool specified, HttpClient http, TransferMeter meter, CancellationToken ct)
	{
		string choice;
		if (specified)
		{
			choice = TexturePackChoice.Normalize(savedChoice);
		}
		else
		{
			string enabled = TexturePackChoice.ReadEnabled(gameDir);
			if (string.IsNullOrWhiteSpace(enabled))
			{
				return Task.CompletedTask;
			}
			choice = TexturePackChoice.Detect(enabled);
		}
		if (!string.Equals(choice, TexturePackChoice.Alacrity, StringComparison.Ordinal))
		{
			return Task.CompletedTask;
		}
		return KnownVisualAssets.EnsureAsync(http, gameDir, meter, ct, includeAlacrity: true, alacrityOnly: true);
	}

	public static async Task ApplyTierAsync(string gameDir, GraphicsTier tier, GraphicsService graphics, HttpClient http, TransferMeter meter, CancellationToken ct)
	{
		byte[] before = SnapshotOptions(gameDir);
		try
		{
			await KnownVisualAssets.EnsureAsync(http, gameDir, meter, ct, includeAlacrity: true, alacrityOnly: true).ConfigureAwait(false);
			graphics.Apply(gameDir, tier);
		}
		catch
		{
			RestoreOptions(gameDir, before);
			throw;
		}
	}

	private static byte[] SnapshotOptions(string gameDir)
	{
		string path = Path.Combine(gameDir, "options.txt");
		if (!File.Exists(path))
		{
			return null;
		}
		return File.ReadAllBytes(path);
	}

	private static void RestoreOptions(string gameDir, byte[] before)
	{
		string path = Path.Combine(gameDir, "options.txt");
		try
		{
			if (before == null)
			{
				if (File.Exists(path))
				{
					File.Delete(path);
				}
				return;
			}
			Directory.CreateDirectory(gameDir);
			File.WriteAllBytes(path, before);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			LauncherLog.Write("texture pack restore " + ex.GetType().Name);
		}
	}
}
