using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public sealed class MinecraftLaunchService
{
	private readonly HttpClient _http;

	public MinecraftLaunchService(HttpClient http)
	{
		_http = http;
	}

	public async Task<Process> LaunchAsync(string java, string gameDir, MinecraftSession session, int ramMb, TransferMeter meter, CancellationToken ct)
	{
		TransferMeter meter2 = meter;
		string gameDir2 = gameDir;
		RequireOnline(session);
		if (StartupHeal.GameLockAlive(NorcomPaths.GameLockPath))
		{
			throw new InvalidOperationException("Minecraft is already running.");
		}
		string fabricId = NorcomPaths.FabricVersionId;
		string vanillaPath = Path.Combine(gameDir2, "versions", "26.3", "26.3.json");
		string fabricPath = Path.Combine(gameDir2, "versions", fabricId, fabricId + ".json");
		meter2.SetPhase("Minecraft 26.3", 0.02, 0.05);
		await EnsureTextAsync("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", async delegate(string body)
		{
			using JsonDocument doc = JsonDocument.Parse(body);
			string @string = doc.RootElement.GetProperty("versions").EnumerateArray().First((JsonElement v) => v.GetProperty("id").GetString() == "26.3")
				.GetProperty("url")
				.GetString();
			await HttpDownload.DownloadAsync(_http, @string, vanillaPath, meter2, ct).ConfigureAwait(continueOnCapturedContext: false);
		}, vanillaPath, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (!File.Exists(fabricPath))
		{
			meter2.SetPhase("Fabric 0.20.1", 0.08, 0.04);
			await HttpDownload.DownloadAsync(_http, NorcomPaths.FabricProfileUrl, fabricPath, meter2, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		string vanillaJson = await File.ReadAllTextAsync(vanillaPath, ct).ConfigureAwait(continueOnCapturedContext: false);
		string fabricJson = await File.ReadAllTextAsync(fabricPath, ct).ConfigureAwait(continueOnCapturedContext: false);
		string natives = Path.Combine(gameDir2, "natives");
		Directory.CreateDirectory(Path.Combine(natives, "java"));
		Directory.CreateDirectory(Path.Combine(natives, "jna"));
		Directory.CreateDirectory(Path.Combine(natives, "lwjgl"));
		Directory.CreateDirectory(Path.Combine(natives, "netty"));
		LaunchPlan draft = LaunchPlanner.Build(vanillaJson, fabricJson, new LaunchContext
		{
			GameDir = gameDir2,
			AssetsDir = Path.Combine(gameDir2, "assets"),
			NativesDir = natives,
			VersionName = fabricId,
			PlayerName = session.Name,
			Uuid = session.Uuid,
			AccessToken = session.AccessToken,
			UserType = session.UserType,
			Xuid = session.Xuid,
			RamMb = ramMb
		});
		await DownloadLibrariesAsync(gameDir2, draft, meter2, ct).ConfigureAwait(continueOnCapturedContext: false);
		string clientJar = await DownloadClientAsync(gameDir2, vanillaJson, meter2, ct).ConfigureAwait(continueOnCapturedContext: false);
		string logConfig = await DownloadLogConfigAsync(gameDir2, vanillaJson, meter2, ct).ConfigureAwait(continueOnCapturedContext: false);
		await DownloadAssetsAsync(gameDir2, vanillaJson, meter2, ct).ConfigureAwait(continueOnCapturedContext: false);
		string classpath = string.Join(';', draft.Libraries.Select((LibraryFile l) => Path.Combine(gameDir2, "libraries", l.RelativePath.Replace('/', Path.DirectorySeparatorChar)))) + ";" + clientJar;
		LaunchPlan launchPlan = LaunchPlanner.Build(vanillaJson, fabricJson, new LaunchContext
		{
			GameDir = gameDir2,
			AssetsDir = Path.Combine(gameDir2, "assets"),
			NativesDir = natives,
			Classpath = classpath,
			VersionName = fabricId,
			PlayerName = session.Name,
			Uuid = session.Uuid,
			AccessToken = session.AccessToken,
			UserType = session.UserType,
			Xuid = session.Xuid,
			RamMb = ramMb,
			LogConfigPath = logConfig
		});
		ProcessStartInfo processStartInfo = new ProcessStartInfo
		{
			FileName = java,
			WorkingDirectory = gameDir2,
			UseShellExecute = false
		};
		foreach (string jvmArg in launchPlan.JvmArgs)
		{
			processStartInfo.ArgumentList.Add(jvmArg);
		}
		processStartInfo.ArgumentList.Add(launchPlan.MainClass);
		foreach (string gameArg in launchPlan.GameArgs)
		{
			processStartInfo.ArgumentList.Add(gameArg);
		}
		Process process = Process.Start(processStartInfo) ?? throw new InvalidOperationException("Windows did not start Java.");
		StartupHeal.WriteGameLock(NorcomPaths.GameLockPath, process.Id);
		process.EnableRaisingEvents = true;
		process.Exited += delegate
		{
			try
			{
				if (File.Exists(NorcomPaths.GameLockPath))
				{
					File.Delete(NorcomPaths.GameLockPath);
				}
			}
			catch
			{
			}
		};
		LauncherLog.Write("Launched Minecraft pid=" + process.Id + " version=" + fabricId + " userType=" + session.UserType);
		meter2.SetPhase("Minecraft is starting", 1.0, 0.0);
		return process;
	}

	public static void RequireOnline(MinecraftSession session)
	{
		if (!OnlineSession.IsPlayable(session))
		{
			throw new InvalidOperationException("Sign in with Microsoft before Play. NORCOM did not start Minecraft.");
		}
	}

	private async Task DownloadLibrariesAsync(string gameDir, LaunchPlan plan, TransferMeter meter, CancellationToken ct)
	{
		int i = 0;
		foreach (LibraryFile lib in plan.Libraries)
		{
			ct.ThrowIfCancellationRequested();
			i++;
			string dest = Path.Combine(gameDir, "libraries", lib.RelativePath.Replace('/', Path.DirectorySeparatorChar));
			meter.SetPhase($"Libraries {i}/{plan.Libraries.Count}", 0.15 + 0.25 * ((double)i / (double)plan.Libraries.Count), 0.02);
			if (!(await MatchesAsync(dest, lib.Sha1, ct).ConfigureAwait(continueOnCapturedContext: false)))
			{
				await HttpDownload.DownloadAsync(_http, lib.Url, dest, meter, ct).ConfigureAwait(continueOnCapturedContext: false);
				bool flag = !string.IsNullOrWhiteSpace(lib.Sha1);
				if (flag)
				{
					flag = !(await MatchesAsync(dest, lib.Sha1, ct).ConfigureAwait(continueOnCapturedContext: false));
				}
				if (flag)
				{
					throw new InvalidDataException("Library hash mismatch: " + lib.Name);
				}
			}
		}
	}

	private async Task<string> DownloadClientAsync(string gameDir, string vanillaJson, TransferMeter meter, CancellationToken ct)
	{
		using JsonDocument doc = JsonDocument.Parse(vanillaJson);
		JsonElement client = doc.RootElement.GetProperty("downloads").GetProperty("client");
		string dest = Path.Combine(gameDir, "versions", "26.3", "26.3.jar");
		string @string = client.GetProperty("sha1").GetString();
		meter.SetPhase("Minecraft client", 0.42, 0.12);
		if (!(await MatchesAsync(dest, @string, ct).ConfigureAwait(continueOnCapturedContext: false)))
		{
			await HttpDownload.DownloadAsync(_http, client.GetProperty("url").GetString(), dest, meter, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		return dest;
	}

	private async Task<string> DownloadLogConfigAsync(string gameDir, string vanillaJson, TransferMeter meter, CancellationToken ct)
	{
		using JsonDocument doc = JsonDocument.Parse(vanillaJson);
		if (!doc.RootElement.TryGetProperty("logging", out var value))
		{
			return "";
		}
		JsonElement file = value.GetProperty("client").GetProperty("file");
		string dest = Path.Combine(gameDir, "assets", "log_configs", file.GetProperty("id").GetString());
		if (!(await MatchesAsync(dest, file.GetProperty("sha1").GetString(), ct).ConfigureAwait(continueOnCapturedContext: false)))
		{
			await HttpDownload.DownloadAsync(_http, file.GetProperty("url").GetString(), dest, meter, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		return dest;
	}

	private async Task DownloadAssetsAsync(string gameDir, string vanillaJson, TransferMeter meter, CancellationToken ct)
	{
		string gameDir2 = gameDir;
		TransferMeter meter2 = meter;
		using JsonDocument doc = JsonDocument.Parse(vanillaJson);
		JsonElement index = doc.RootElement.GetProperty("assetIndex");
		string @string = index.GetProperty("id").GetString();
		string indexPath = Path.Combine(gameDir2, "assets", "indexes", @string + ".json");
		meter2.SetPhase("Asset index", 0.56, 0.04);
		if (!(await MatchesAsync(indexPath, index.GetProperty("sha1").GetString(), ct).ConfigureAwait(continueOnCapturedContext: false)))
		{
			await HttpDownload.DownloadAsync(_http, index.GetProperty("url").GetString(), indexPath, meter2, ct).ConfigureAwait(continueOnCapturedContext: false);
		}
		using JsonDocument indexDoc = JsonDocument.Parse(await File.ReadAllTextAsync(indexPath, ct).ConfigureAwait(continueOnCapturedContext: false));
		JsonElement property = indexDoc.RootElement.GetProperty("objects");
		List<(string, long)> list = new List<(string, long)>();
		foreach (JsonProperty item in property.EnumerateObject())
		{
			string string2 = item.Value.GetProperty("hash").GetString();
			long @int = item.Value.GetProperty("size").GetInt64();
			string text = Path.Combine(gameDir2, "assets", "objects", string2.Substring(0, 2), string2);
			if (!File.Exists(text) || new FileInfo(text).Length != @int)
			{
				list.Add((string2, @int));
			}
		}
		int finished = 0;
		int total = Math.Max(1, list.Count);
		SemaphoreSlim gate = new SemaphoreSlim(8);
		try
		{
			await Task.WhenAll(list.Select<(string, long), Task>(async delegate((string hash, long size) item)
			{
				await gate.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
				try
				{
					string dest = Path.Combine(gameDir2, "assets", "objects", item.hash.Substring(0, 2), item.hash);
					string url = "https://resources.download.minecraft.net/" + item.hash.Substring(0, 2) + "/" + item.hash;
					await HttpDownload.DownloadAsync(_http, url, dest, meter2, ct).ConfigureAwait(continueOnCapturedContext: false);
					int num = Interlocked.Increment(ref finished);
					if (num % 25 == 0 || num == total)
					{
						meter2.SetPhase($"Assets {num}/{total}", 0.6 + 0.35 * ((double)num / (double)total), 0.01);
					}
				}
				finally
				{
					gate.Release();
				}
			})).ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			if (gate != null)
			{
				((IDisposable)gate).Dispose();
			}
		}
	}

	private async Task EnsureTextAsync(string manifestUrl, Func<string, Task> whenMissing, string dest, CancellationToken ct)
	{
		if (!File.Exists(dest))
		{
			await whenMissing(await _http.GetStringAsync(manifestUrl, ct).ConfigureAwait(continueOnCapturedContext: false)).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	private static async Task<bool> MatchesAsync(string path, string? sha1, CancellationToken ct)
	{
		if (!File.Exists(path))
		{
			return false;
		}
		if (string.IsNullOrWhiteSpace(sha1))
		{
			return true;
		}
		return Hashing.EqualsHex(await Hashing.FileHexAsync(path, "sha1", ct).ConfigureAwait(continueOnCapturedContext: false), sha1);
	}
}
