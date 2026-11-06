using Xunit;
using System.IO.Compression;
using System.Security.Cryptography;
using Norcom.Launcher.Core;
using Norcom.Launcher.Core.Models;
using Norcom.Launcher.Core.Services;
using Norcom.Launcher.Core.Text;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Norcom.Launcher.Tests;

public class PlayUpdateTests
{
	[Fact]
	public void MandatoryAndAvailableBothApplyOnPlay()
	{
		LauncherManifest newer = new LauncherManifest { Version = "2.0.40", MinVersion = "2.0.0", Sha256 = new string('a', 64), ZipUrl = "https://example.test/launcher.zip" };
		Assert.Equal(UpdateKind.Available, SelfUpdateService.Decide("2.0.32", newer, false));
		Assert.True(SelfUpdateService.ShouldApply("2.0.32", newer, false, null));
		LauncherManifest forced = new LauncherManifest { Version = "2.0.40", MinVersion = "2.0.40", Sha256 = new string('b', 64), ZipUrl = "https://example.test/launcher.zip" };
		Assert.Equal(UpdateKind.Mandatory, SelfUpdateService.Decide("2.0.32", forced, false));
		Assert.True(SelfUpdateService.ShouldApply("2.0.32", forced, false, null));
		LauncherManifest same = new LauncherManifest { Version = "2.0.32", MinVersion = "2.0.0", Sha256 = new string('c', 64), ZipUrl = "https://example.test/launcher.zip" };
		Assert.Equal(UpdateKind.None, SelfUpdateService.Decide("2.0.32", same, false));
		Assert.False(SelfUpdateService.ShouldApply("2.0.32", same, false, new HealHint { MinLauncher = "9.0.0" }));
		Assert.True(SelfUpdateService.ShouldApply("2.0.32", newer, false, new HealHint { MinLauncher = "9.0.0" }));
	}

	[Fact]
	public async Task FeedCheckAppliesANewerLauncherInsteadOfOnlyLogging()
	{
		string root = Path.Combine(Path.GetTempPath(), "norcom-playupd-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(root);
		string exe = Path.Combine(root, "NorcomLauncher.exe");
		await File.WriteAllTextAsync(exe, "exe");
		string zip = Path.Combine(root, "NorcomWpf-2.0.40.zip");
		using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
		{
			archive.CreateEntryFromFile(exe, "NorcomLauncher.exe");
			ZipArchiveEntry version = archive.CreateEntry("payload-version.txt");
			using StreamWriter writer = new StreamWriter(version.Open());
			writer.Write("2.0.40\n");
		}
		string sha = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(zip))).ToLowerInvariant();
		string manifestPath = Path.Combine(root, "manifest.json");
		await File.WriteAllTextAsync(manifestPath, "{\"version\":\"2.0.40\",\"minVersion\":\"2.0.0\",\"sha256\":\"" + sha + "\",\"zipUrl\":\"" + zip.Replace("\\", "\\\\") + "\"}");
		string? previousManifest = Environment.GetEnvironmentVariable(SelfUpdateService.ManifestOverrideVariable);
		bool previousExit = PlayUpdateGate.ExitAfterSchedule;
		Action<string, string, string>? previousScheduled = SelfUpdateService.PlayScheduled;
		Func<string, string?> previousExe = SelfUpdateService.ExeVersionOf;
		string? scheduled = null;
		try
		{
			Environment.SetEnvironmentVariable(SelfUpdateService.ManifestOverrideVariable, manifestPath);
			PlayUpdateGate.ExitAfterSchedule = false;
			SelfUpdateService.ExeVersionOf = _ => "2.0.40";
			SelfUpdateService.PlayScheduled = (_, version, hash) => scheduled = version + " " + hash;
			string hint = "{\"schema\":1,\"pin\":\"" + PinGate.LivePin + "\",\"mc\":\"26.3\",\"loader\":\"0.20.1\",\"minLauncher\":\"9.0.0\",\"repairAction\":\"resync-pack\",\"source\":\"admin\"}";
			using HttpClient http = new(new HintAndPin(hint, SlimPin()));
			string game = Path.Combine(root, "game");
			Directory.CreateDirectory(game);
			await File.WriteAllTextAsync(Path.Combine(game, PinGate.AppliedPinFile), PinGate.LivePin + "\n");
			await PinHeal.RunAsync(http, game, TomlLite.Parse("version = \"1.0.35\"\n"), null, _ =>
			{
				File.WriteAllText(Path.Combine(game, PinGate.AppliedPinFile), PinGate.LivePin + "\n");
				return Task.CompletedTask;
			}, CancellationToken.None);
			Assert.Equal("2.0.40 " + sha, scheduled);
		}
		finally
		{
			Environment.SetEnvironmentVariable(SelfUpdateService.ManifestOverrideVariable, previousManifest);
			PlayUpdateGate.ExitAfterSchedule = previousExit;
			SelfUpdateService.PlayScheduled = previousScheduled;
			SelfUpdateService.ExeVersionOf = previousExe;
		}
	}

	[Fact]
	public void BeforePlayStillCleansObsoleteLauncherCopies()
	{
		string root = Path.Combine(Path.GetTempPath(), "norcom-playclean-" + Guid.NewGuid().ToString("n"));
		string home = Path.Combine(root, "NORCOM");
		string live = Path.Combine(home, "Launcher", "NorcomLauncher.exe");
		Directory.CreateDirectory(Path.GetDirectoryName(live)!);
		File.WriteAllText(live, "live");
		string stale = Path.Combine(home, "recovery", "self-update-2.0.31");
		Directory.CreateDirectory(stale);
		File.WriteAllText(Path.Combine(stale, "NorcomLauncher.previous.exe"), "old");
		string admin = Path.Combine(root, "Desktop", "run32-admin-snappy");
		Directory.CreateDirectory(admin);
		File.WriteAllText(Path.Combine(admin, "NorcomLauncher.exe"), "admin");
		LauncherCleanup.RemoveObsolete(live, home, Path.Combine(root, ".norcom"), Path.Combine(root, "temp"));
		Assert.False(Directory.Exists(stale));
		Assert.Equal("live", File.ReadAllText(live));
		Assert.Equal("admin", File.ReadAllText(Path.Combine(admin, "NorcomLauncher.exe")));
	}

	private static string SlimPin()
	{
		return "{\"schema\":1,\"mc\":\"26.3\",\"loader\":\"0.20.1\",\"pin\":\"" + PinGate.LivePin + "\",\"source\":\"server\",\"updatedUtc\":\"2026-11-06T02:04:49Z\"}";
	}

	private sealed class HintAndPin : HttpMessageHandler
	{
		private readonly string _hint;
		private readonly string _pin;

		public HintAndPin(string hint, string pin)
		{
			_hint = hint;
			_pin = pin;
		}

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			string name = request.RequestUri?.Segments.Last() ?? "";
			if (name == "heal-hint.json")
			{
				return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
				{
					Content = new StringContent(_hint)
				});
			}
			if (name is "server-compat.json" or "norcom-mod-pin.json" or "server-pin.json" or "manifest.json")
			{
				return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
			}
			return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
			{
				Content = new StringContent(_pin)
			});
		}
	}
}
