using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public static class KnownVisualAssets
{
	public static readonly VisualAsset[] All = new VisualAsset[5]
	{
		new VisualAsset("shaderpacks/ComplementaryUnbound_r5.9.3.zip", "https://cdn.modrinth.com/data/R6NEzAwj/versions/B1kyfoUZ/ComplementaryUnbound_r5.9.3.zip", "a085d1d8cda67cd17a2b414eeba48bbd809da069f269bab46246235ec8fe714a275013c70d0504527b0987d810deddddba4564332414ab3b3bd7435932ff808c"),
		new VisualAsset("resourcepacks/Patrix_26.3_32x_basic.zip", "https://cdn.modrinth.com/data/olO1TaXd/versions/V9k0Ak3w/Patrix_26.3_32x_basic.zip", "ad03f836285a0a894b0ed5d64ee3c910a67dd63c743e44f6f4330d32223dbff27795f7773d6ea6fb283591b489770822f796ce9184ac3f69663dc5a0cf4d0352"),
		new VisualAsset("resourcepacks/Patrix_26.3_32x_addon.zip", "https://cdn.modrinth.com/data/olO1TaXd/versions/V9k0Ak3w/Patrix_26.3_32x_addon.zip", "b739041ad699ea3e532724ffabd128567f5f0686afbd259ba8eb7f88d9f194781cadeebfa9b18d46f6de9a453bf674d78b9d806a950eddcf11ec4fccb320333f"),
		new VisualAsset("resourcepacks/Patrix_26.3_models.zip", "https://cdn.modrinth.com/data/olO1TaXd/versions/V9k0Ak3w/Patrix_26.3_models.zip", "39d4e6fd92d1daa9fb515ed59f57dc3d09ba5b2067c9f4bc0639447dc74079c9aff46fa1138495074ee3b1eb6ef2b6bbd85d5c2dd576cf69656f9b62163067dd"),
		new VisualAsset("resourcepacks/Alacrity.zip", AlacrityUrl, AlacritySha512)
	};

	// Tests substitute a stand-in asset. Production uses All. Alacrity.zip is All Rights Reserved and is never stored in this repo.
	internal static Func<VisualAsset[]> Assets = () => All;

	public const string AlacrityUrl = "https://cdn.modrinth.com/data/PUUpX2qq/versions/Kc2ktOzs/Alacrity.zip";

	public const string AlacritySha512 = "0b0faa45553153000b4cbb187672f49d06e19b4212f3c490964cc91e6e97767ba00ab42a30c4b77ab52994883f9a2a1251f98fd383d59cfa07ce5793d1452822";

	public static async Task EnsureMarbleAsync(HttpClient http, string gameDir, TransferMeter meter, CancellationToken ct)
	{
		string dest = Path.Combine(gameDir, "resourcepacks", GraphicsPack.MarbleFile);
		Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
		if (IsLiveMarble(dest))
		{
			return;
		}
		string temp = Path.Combine(Path.GetTempPath(), "norcom-marble-" + Guid.NewGuid().ToString("n") + ".zip");
		try
		{
			meter.SetPhase("Downloading norcom_marble.zip", Math.Max(meter.Snapshot().Fraction, 0.9), 0.02);
			await HttpDownload.DownloadAsync(http, GraphicsPack.MarbleUrl, temp, meter, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (!IsLiveMarble(temp))
			{
				LauncherLog.Write("marble download refused superseded pack");
				return;
			}
			File.Move(temp, dest, overwrite: true);
			LauncherLog.Write("marble downloaded");
		}
		catch (Exception ex)
		{
			LauncherLog.Write("marble download " + ex.GetType().Name);
		}
		finally
		{
			try
			{
				if (File.Exists(temp))
				{
					File.Delete(temp);
				}
			}
			catch (Exception ex)
			{
				LauncherLog.Write("marble download " + ex.GetType().Name);
			}
		}
	}

	private static bool IsLiveMarble(string path)
	{
		if (!File.Exists(path))
		{
			return false;
		}
		FileInfo info = new FileInfo(path);
		if (info.Length != GraphicsPack.MarbleBytes)
		{
			return false;
		}
		return Hashing.EqualsHex(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))), GraphicsPack.MarbleSha256);
	}

	public static bool WantsAlacrity(string gameDir)
	{
		string enabled = TexturePackChoice.ReadEnabled(gameDir);
		if (string.IsNullOrWhiteSpace(enabled))
		{
			return false;
		}
		return string.Equals(TexturePackChoice.Detect(enabled), TexturePackChoice.Alacrity, StringComparison.Ordinal);
	}

	public static Task EnsureAsync(HttpClient http, string gameDir, TransferMeter meter, CancellationToken ct)
	{
		return EnsureAsync(http, gameDir, meter, ct, WantsAlacrity(gameDir), alacrityOnly: false);
	}

	public static async Task EnsureAsync(HttpClient http, string gameDir, TransferMeter meter, CancellationToken ct, bool includeAlacrity, bool alacrityOnly = false)
	{
		VisualAsset[] all = Assets() ?? All;
		int count = Math.Max(1, all.Length);
		for (int i = 0; i < all.Length; i++)
		{
			VisualAsset asset = all[i];
			bool alacrity = IsAlacrity(asset);
			if (alacrity && !includeAlacrity)
			{
				continue;
			}
			if (alacrityOnly && !alacrity)
			{
				continue;
			}
			meter.SetPhase("Checking " + Path.GetFileName(asset.RelativePath), 0.2 + 0.7 * ((double)(i + 1) / (double)count), 0.05);
			await InstallVerifiedAsync(http, gameDir, asset, meter, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		if (includeAlacrity)
		{
			GraphicsService.HealOverlay(gameDir);
		}
	}

	private static async Task InstallVerifiedAsync(HttpClient http, string gameDir, VisualAsset asset, TransferMeter meter, CancellationToken ct)
	{
		string dest = Path.Combine(gameDir, asset.RelativePath.Replace('/', Path.DirectorySeparatorChar));
		if (File.Exists(dest) && Hashing.EqualsHex(await Hashing.FileHexAsync(dest, "sha512", ct).ConfigureAwait(continueOnCapturedContext: false), asset.Sha512))
		{
			return;
		}
		string name = Path.GetFileName(asset.RelativePath);
		string staging = dest + ".norcom-download";
		DeleteQuiet(staging);
		DeleteQuiet(staging + ".part");
		try
		{
			meter.SetPhase("Downloading " + name, meter.Snapshot().Fraction, 0.1);
			await HttpDownload.DownloadAsync(http, asset.Url, staging, meter, ct).ConfigureAwait(continueOnCapturedContext: false);
			if (!Hashing.EqualsHex(await Hashing.FileHexAsync(staging, "sha512", ct).ConfigureAwait(continueOnCapturedContext: false), asset.Sha512))
			{
				DeleteQuiet(staging);
				throw new InvalidDataException(name + " did not match the official Modrinth file. The texture pack list was left as it was.");
			}
			string parent = Path.GetDirectoryName(dest);
			if (!string.IsNullOrEmpty(parent))
			{
				Directory.CreateDirectory(parent);
			}
			File.Move(staging, dest, overwrite: true);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			DeleteQuiet(staging);
			DeleteQuiet(staging + ".part");
			if (ex is InvalidDataException)
			{
				throw;
			}
			string detail = DownloadStopped(ex)
				? " download did not finish. The texture pack list was left as it was."
				: " could not be downloaded. Check the connection. The texture pack list was left as it was.";
			throw new InvalidDataException(name + detail, ex);
		}
	}

	private static bool DownloadStopped(Exception ex)
	{
		for (Exception cursor = ex; cursor != null; cursor = cursor.InnerException)
		{
			if (cursor is TimeoutException || cursor is IOException)
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsAlacrity(VisualAsset asset)
	{
		return asset.RelativePath.EndsWith("/Alacrity.zip", StringComparison.OrdinalIgnoreCase) || asset.RelativePath.Equals("Alacrity.zip", StringComparison.OrdinalIgnoreCase);
	}

	private static void DeleteQuiet(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
		}
	}
}
