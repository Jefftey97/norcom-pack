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
	public async Task HealFailTwiceRefuses()
	{
		string dir = NewDir();
		await File.WriteAllTextAsync(Path.Combine(dir, PinGate.AppliedPinFile), "old-pin\n");
		int calls = 0;
		using HttpClient http = new(new PinHttp(Slim()));
		PinMismatchException ex = await Assert.ThrowsAsync<PinMismatchException>(() => PinHeal.RunAsync(http, dir, TomlLite.Parse(Pack), null, _ =>
		{
			calls++;
			return Task.CompletedTask;
		}, CancellationToken.None));
		Assert.Equal(PinGate.BlockCopy, ex.Message);
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

		public PinHttp(string pin)
		{
			_pin = pin;
		}

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			string name = request.RequestUri?.Segments.Last() ?? "";
			if (name is "heal-hint.json" or "server-compat.json" or "norcom-mod-pin.json")
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
