using Xunit;
using System.Net;
using System.Text;
using System.Text.Json;
using Norcom.Launcher.Core.Services;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Tests;

public class PinHealTests
{
	private const string Pack = """
		version = "1.0.35"
		[versions]
		minecraft = "26.3"
		fabric = "0.20.1"
		""";

	[Fact]
	public void ResyncPackGetsTwoAttempts()
	{
		Assert.Equal(2, PinHeal.MaxAttempts("resync-pack"));
		Assert.Equal(2, PinHeal.MaxAttempts(null));
		Assert.Equal(1, PinHeal.MaxAttempts("stop"));
	}

	[Fact]
	public void FeedBehindMinLauncherNeedsUpdate()
	{
		HealHint hint = new HealHint { MinLauncher = "9.0.0", PackVersion = "1.0.35", RepairAction = "resync-pack" };
		Assert.True(PinHeal.NeedsUpdate("2.0.26", "1.0.35", hint));
		Assert.False(PinHeal.NeedsUpdate("9.0.0", "1.0.35", hint));
	}

	private const string LiveHint = """
		{"schema":1,"pin":"fb8252c337d61ace29bf49bd0a1b953c4c6f72146f9971aeb9b5ddcb6198bb73","mc":"26.3","loader":"0.20.1","minLauncher":"2.0.1","repairAction":"resync-pack","updatedUtc":"2026-11-06T02:46:16Z","source":"admin"}
		""";

	[Fact]
	public async Task LiveHealHintIsFetchedAfterThePinFiles()
	{
		string dir = NewDir();
		string save = Path.Combine(dir, "saves", "world", "level.dat");
		Directory.CreateDirectory(Path.GetDirectoryName(save)!);
		await File.WriteAllTextAsync(save, "keep");
		await File.WriteAllTextAsync(Path.Combine(dir, PinGate.AppliedPinFile), "old-pin\n");
		List<string> seen = new List<string>();
		int calls = 0;
		using HttpClient http = new(new PinHttp(Slim(), LiveHint, seen));
		HealHint hint = await PinHeal.FetchHintAsync(http, CancellationToken.None);
		int before = seen.Count;
		await PinHeal.RunAsync(http, dir, TomlLite.Parse(Pack), null, _ =>
		{
			calls++;
			File.WriteAllText(Path.Combine(dir, PinGate.AppliedPinFile), PinGate.LivePin + "\n");
			return Task.CompletedTask;
		}, CancellationToken.None);
		Assert.Equal("resync-pack", hint.RepairAction);
		Assert.Equal(PinGate.LivePin, hint.Pin);
		Assert.Equal("2.0.1", hint.MinLauncher);
		Assert.Equal("26.3", hint.Mc);
		Assert.Equal("0.20.1", hint.Loader);
		Assert.Equal("admin", hint.Source);
		Assert.Equal("2026-11-06T02:46:16Z", hint.UpdatedUtc);
		Assert.False(PinHeal.NeedsUpdate("2.0.26", "1.0.35", hint));
		Assert.True(PinHeal.NeedsUpdate("2.0.0", "1.0.35", hint));
		Assert.False(PackSyncService.IsSafeToDelete("saves/world/level.dat"));
		Assert.Equal("keep", await File.ReadAllTextAsync(save));
		Assert.Equal(1, calls);
		List<string> play = seen.GetRange(before, seen.Count - before);
		int pinAt = play.FindIndex(name => name is "norcom-mod-pin.json" or "server-pin.json" or "server-compat.json");
		int hintAt = play.FindIndex(name => name == "heal-hint.json");
		Assert.True(pinAt >= 0 && hintAt > pinAt);
	}

	[Fact]
	public async Task BehindMinLauncherChecksTheFeedThenHeals()
	{
		string dir = NewDir();
		await File.WriteAllTextAsync(Path.Combine(dir, PinGate.AppliedPinFile), "old-pin\n");
		string hint = LiveHint.Replace("\"minLauncher\":\"2.0.1\"", "\"minLauncher\":\"9.0.0\"");
		List<string> seen = new List<string>();
		bool synced = false;
		using HttpClient http = new(new PinHttp(Slim(), hint, seen));
		await PinHeal.RunAsync(http, dir, TomlLite.Parse(Pack), null, _ =>
		{
			synced = true;
			File.WriteAllText(Path.Combine(dir, PinGate.AppliedPinFile), PinGate.LivePin + "\n");
			return Task.CompletedTask;
		}, CancellationToken.None);
		Assert.Contains("manifest.json", seen);
		int manifestAt = seen.IndexOf("manifest.json");
		Assert.True(manifestAt >= 0);
		Assert.True(synced);
		Assert.Equal(PinGate.LivePin, PinGate.ReadAppliedPin(dir));
	}

	[Fact]
	public async Task MismatchTriggersHealThenAllowsPlay()
	{
		string dir = NewDir();
		await File.WriteAllTextAsync(Path.Combine(dir, PinGate.AppliedPinFile), "old-pin\n");
		int calls = 0;
		using HttpClient http = new(new PinHttp(Slim()));
		await PinHeal.RunAsync(http, dir, TomlLite.Parse(Pack), null, _ =>
		{
			calls++;
			File.WriteAllText(Path.Combine(dir, PinGate.AppliedPinFile), PinGate.LivePin + "\n");
			return Task.CompletedTask;
		}, CancellationToken.None);
		Assert.Equal(1, calls);
		Assert.Equal(PinGate.LivePin, PinGate.ReadAppliedPin(dir));
	}

	[Fact]
	public async Task HealWritesThePublishedPinAfterTheJarSync()
	{
		string published = new string('c', 64);
		string dir = NewDir();
		await File.WriteAllTextAsync(Path.Combine(dir, PinGate.AppliedPinFile), "old-pin\n");
		string slim = JsonSerializer.Serialize(new { schema = 1, mc = "26.3", loader = "0.20.1", pin = published, source = "server" });
		int calls = 0;
		using HttpClient http = new(new PinHttp(slim));
		await PinHeal.RunAsync(http, dir, TomlLite.Parse(Pack), null, _ =>
		{
			calls++;
			return Task.CompletedTask;
		}, CancellationToken.None);
		Assert.Equal(1, calls);
		Assert.Equal(published, PinGate.ReadAppliedPin(dir));
		Assert.NotEqual(PinGate.LivePin, published);
	}

	[Fact]
	public async Task HealFailTwiceRefuses()
	{
		string dir = NewDir();
		await File.WriteAllTextAsync(Path.Combine(dir, PinGate.AppliedPinFile), "old-pin\n");
		int calls = 0;
		using HttpClient http = new(new PinHttp(Slim()));
		PinMismatchException ex = await Assert.ThrowsAsync<PinMismatchException>(() => PinHeal.RunAsync(http, dir, TomlLite.Parse(Pack), null, _ =>
		{
			calls++;
			throw new InvalidOperationException("sync broke");
		}, CancellationToken.None));
		Assert.Contains("pack resync", ex.Message);
		Assert.Contains(PinGate.BlockCopy, ex.Message);
		Assert.Equal(2, calls);
		Assert.Equal("old-pin", PinGate.ReadAppliedPin(dir));
	}

	private static string Slim()
	{
		return JsonSerializer.Serialize(new
		{
			schema = 1,
			mc = "26.3",
			loader = "0.20.1",
			pin = PinGate.LivePin,
			source = "server",
			updatedUtc = "2026-11-06T02:04:49Z"
		});
	}

	private static string NewDir()
	{
		string dir = Path.Combine(Path.GetTempPath(), "norcom-heal-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(dir);
		return dir;
	}

	private sealed class PinHttp : HttpMessageHandler
	{
		private readonly string _pin;
		private readonly string? _hint;
		private readonly List<string>? _seen;

		public PinHttp(string pin, string? hint = null, List<string>? seen = null)
		{
			_pin = pin;
			_hint = hint;
			_seen = seen;
		}

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			string name = request.RequestUri?.Segments.Last() ?? "";
			_seen?.Add(name);
			if (name == "heal-hint.json" && _hint != null)
			{
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = new StringContent(_hint, Encoding.UTF8, "application/json")
				});
			}
			if (name is "heal-hint.json" or "server-compat.json" or "norcom-mod-pin.json" or "manifest.json")
			{
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
			}
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(_pin, Encoding.UTF8, "application/json")
			});
		}
	}
}
