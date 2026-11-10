using System.Net;
using System.Security.Cryptography;
using System.Text;
using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

public class PackDownloadTests
{
	[Fact]
	public async Task VerifiedDownloadReplacesTheFileAndRemovesThePartial()
	{
		using var host = new Host();
		byte[] body = Encoding.UTF8.GetBytes("new-bytes");
		host.IndexFile("config/keep.txt", body);
		string dest = Path.Combine(host.Game, "config", "keep.txt");
		Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
		await File.WriteAllTextAsync(dest, "old-bytes");
		await host.Sync();
		Assert.Equal("new-bytes", await File.ReadAllTextAsync(dest));
		Assert.Contains("config/keep.txt", await File.ReadAllTextAsync(host.Managed));
		Assert.Empty(Directory.EnumerateFiles(host.Root, "*.norcom-partial", SearchOption.AllDirectories));
		Assert.Empty(Directory.EnumerateFiles(host.Root, "*.part", SearchOption.AllDirectories));
	}

	[Fact]
	public async Task HashMismatchLeavesTheExistingFile()
	{
		using var host = new Host();
		host.IndexFile("config/keep.txt", Encoding.UTF8.GetBytes("good-bytes"), Encoding.UTF8.GetBytes("bad-bytes"));
		string dest = Path.Combine(host.Game, "config", "keep.txt");
		Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
		await File.WriteAllTextAsync(dest, "keep-me");
		await Assert.ThrowsAsync<PinMismatchException>(() => host.Sync());
		Assert.Equal("keep-me", await File.ReadAllTextAsync(dest));
		Assert.Empty(Directory.EnumerateFiles(host.Root, "*.norcom-partial", SearchOption.AllDirectories));
		Assert.Empty(Directory.EnumerateFiles(host.Root, "*.part", SearchOption.AllDirectories));
	}

	[Fact]
	public async Task MissingFileHashDoesNotWrite()
	{
		using var host = new Host();
		host.Index("[[files]]\nfile = \"config/keep.txt\"\n");
		string dest = Path.Combine(host.Game, "config", "keep.txt");
		await Assert.ThrowsAsync<PinMismatchException>(() => host.Sync());
		Assert.False(File.Exists(dest));
	}

	[Fact]
	public async Task MissingIndexHashDoesNotDownloadTheIndex()
	{
		using var host = new Host();
		host.PackHash = "";
		host.Files["https://pack.example/index.toml"] = Encoding.UTF8.GetBytes("[[files]]\nfile = \"config/keep.txt\"\nhash = \"aa\"\n");
		await Assert.ThrowsAsync<PinMismatchException>(() => host.Sync());
		Assert.False(File.Exists(Path.Combine(host.Cache, "index.toml")));
		Assert.False(host.Files.ContainsKey("seen:https://pack.example/index.toml"));
	}

	[Fact]
	public async Task IndexHashMismatchKeepsTheCachedIndex()
	{
		using var host = new Host();
		host.Index("[[files]]\nfile = \"config/keep.txt\"\nhash = \"" + new string('a', 64) + "\"\n");
		host.PackHash = new string('b', 64);
		await File.WriteAllTextAsync(Path.Combine(host.Cache, "index.toml"), "stale-index");
		await Assert.ThrowsAsync<PinMismatchException>(() => host.Sync());
		Assert.Equal("stale-index", await File.ReadAllTextAsync(Path.Combine(host.Cache, "index.toml")));
		Assert.False(File.Exists(Path.Combine(host.Game, "config", "keep.txt")));
	}

	[Fact]
	public async Task EscapingIndexAndModPathsAreNotWritten()
	{
		using var host = new Host();
		byte[] body = Encoding.UTF8.GetBytes("nope");
		string hash = Sha256(body);
		host.Index("[[files]]\nfile = \"../outside.txt\"\nhash = \"" + hash + "\"\n");
		host.Files["https://pack.example/../outside.txt"] = body;
		await Assert.ThrowsAsync<PinMismatchException>(() => host.Sync());
		Assert.False(File.Exists(Path.Combine(host.Root, "outside.txt")));

		byte[] jar = Encoding.UTF8.GetBytes("jar");
		string toml = "filename = \"../outside.jar\"\nside = \"both\"\n[download]\nurl = \"https://cdn.example/demo.jar\"\nhash-format = \"sha512\"\nhash = \"" + Sha512(jar) + "\"\n";
		byte[] tomlBytes = Encoding.UTF8.GetBytes(toml);
		host.Index("[[files]]\nfile = \"mods/demo.pw.toml\"\nhash = \"" + Sha256(tomlBytes) + "\"\n");
		host.Files["https://pack.example/mods/demo.pw.toml"] = tomlBytes;
		host.Files["https://cdn.example/demo.jar"] = jar;
		await Assert.ThrowsAsync<PinMismatchException>(() => host.Sync());
		Assert.False(File.Exists(Path.Combine(host.Root, "outside.jar")));
		Assert.False(File.Exists(Path.Combine(host.Game, "mods", "outside.jar")));
	}

	[Fact]
	public async Task VerifiedModJarLandsBesideItsMetadata()
	{
		using var host = new Host();
		byte[] jar = Encoding.UTF8.GetBytes("jar-bytes");
		string toml = "filename = \"demo.jar\"\nside = \"both\"\n[download]\nurl = \"https://cdn.example/demo.jar\"\nhash-format = \"sha512\"\nhash = \"" + Sha512(jar) + "\"\n";
		byte[] tomlBytes = Encoding.UTF8.GetBytes(toml);
		host.Index("[[files]]\nfile = \"mods/demo.pw.toml\"\nhash = \"" + Sha256(tomlBytes) + "\"\n");
		host.Files["https://pack.example/mods/demo.pw.toml"] = tomlBytes;
		host.Files["https://cdn.example/demo.jar"] = jar;
		await host.Sync();
		Assert.Equal(jar, await File.ReadAllBytesAsync(Path.Combine(host.Game, "mods", "demo.jar")));
	}

	[Fact]
	public async Task DirectorySymlinkInsideTheGameFolderWritesNothingOutside()
	{
		using var host = new Host();
		string outside = Path.Combine(host.Root, "outside");
		Directory.CreateDirectory(outside);
		Directory.CreateSymbolicLink(Path.Combine(host.Game, "mods"), outside);
		host.IndexFile("mods/nested/evil.jar", Encoding.UTF8.GetBytes("evil"));
		await Assert.ThrowsAsync<PinMismatchException>(() => host.Sync());
		Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
		Assert.NotNull(Directory.ResolveLinkTarget(Path.Combine(host.Game, "mods"), returnFinalTarget: false));
	}

	[Fact]
	public async Task FileSymlinkAtTheDestinationIsNotFollowed()
	{
		using var host = new Host();
		string outside = Path.Combine(host.Root, "outside.txt");
		await File.WriteAllTextAsync(outside, "secret");
		Directory.CreateDirectory(Path.Combine(host.Game, "config"));
		string link = Path.Combine(host.Game, "config", "keep.txt");
		File.CreateSymbolicLink(link, outside);
		host.IndexFile("config/keep.txt", Encoding.UTF8.GetBytes("replaced"));
		await Assert.ThrowsAsync<PinMismatchException>(() => host.Sync());
		Assert.Equal("secret", await File.ReadAllTextAsync(outside));
		Assert.NotNull(File.ResolveLinkTarget(link, returnFinalTarget: false));
	}

	[Fact]
	public async Task IndexSymlinkIsNotReplacedOrFollowed()
	{
		using var host = new Host();
		string outside = Path.Combine(host.Root, "index-target");
		await File.WriteAllTextAsync(outside, "secret-index");
		File.CreateSymbolicLink(Path.Combine(host.Cache, "index.toml"), outside);
		host.IndexFile("config/keep.txt", Encoding.UTF8.GetBytes("body"));
		await Assert.ThrowsAsync<PinMismatchException>(() => host.Sync());
		Assert.Equal("secret-index", await File.ReadAllTextAsync(outside));
		Assert.NotNull(File.ResolveLinkTarget(Path.Combine(host.Cache, "index.toml"), returnFinalTarget: false));
		Assert.False(File.Exists(Path.Combine(host.Game, "config", "keep.txt")));
	}

	[Fact]
	public async Task NestedPackPathIsCreatedOneLevelAtATime()
	{
		using var host = new Host();
		byte[] body = Encoding.UTF8.GetBytes("nested");
		host.IndexFile("config/norcom/client.json", body);
		await host.Sync();
		Assert.Equal(body, await File.ReadAllBytesAsync(Path.Combine(host.Game, "config", "norcom", "client.json")));
	}

	[Fact]
	public async Task GameRootJunctionStillReceivesANormalFile()
	{
		using var host = new Host();
		string real = Path.Combine(host.Root, "real-game");
		Directory.CreateDirectory(real);
		Directory.Delete(host.Game);
		Directory.CreateSymbolicLink(host.Game, real);
		byte[] body = Encoding.UTF8.GetBytes("through-root");
		host.IndexFile("config/keep.txt", body);
		await host.Sync();
		Assert.Equal(body, await File.ReadAllBytesAsync(Path.Combine(real, "config", "keep.txt")));
		Assert.NotNull(Directory.ResolveLinkTarget(host.Game, returnFinalTarget: false));
	}

	[Fact]
	public async Task PreserveKeepsAnExistingFileWhenTheIndexHasNoHash()
	{
		using var host = new Host();
		host.Index("[[files]]\nfile = \"config/keep.txt\"\npreserve = true\n");
		string dest = Path.Combine(host.Game, "config", "keep.txt");
		Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
		await File.WriteAllTextAsync(dest, "user edit");
		await host.Sync();
		Assert.Equal("user edit", await File.ReadAllTextAsync(dest));
	}

	private static string Sha256(byte[] data)
	{
		return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
	}

	private static string Sha512(byte[] data)
	{
		return Convert.ToHexString(SHA512.HashData(data)).ToLowerInvariant();
	}

	private sealed class Host : IDisposable
	{
		private readonly Handler _handler = new Handler();
		private readonly HttpClient _http;

		public Host()
		{
			Root = Path.Combine(Path.GetTempPath(), "norcom-pack-sync-" + Guid.NewGuid().ToString("N"));
			Game = Path.Combine(Root, "game");
			Cache = Path.Combine(Root, "cache");
			Managed = Path.Combine(Root, "managed.json");
			Directory.CreateDirectory(Game);
			Directory.CreateDirectory(Cache);
			_http = new HttpClient(_handler);
		}

		public string Root { get; }

		public string Game { get; }

		public string Cache { get; }

		public string Managed { get; }

		public Dictionary<string, byte[]> Files => _handler.Files;

		public string PackHash { get; set; } = "";

		public void Index(string indexBody)
		{
			byte[] bytes = Encoding.UTF8.GetBytes("hash-format = \"sha256\"\n" + indexBody);
			Files["https://pack.example/index.toml"] = bytes;
			PackHash = Sha256(bytes);
		}

		public void IndexFile(string relative, byte[] body)
		{
			IndexFile(relative, body, body);
		}

		public void IndexFile(string relative, byte[] hashed, byte[] served)
		{
			Index("[[files]]\nfile = \"" + relative + "\"\nhash = \"" + Sha256(hashed) + "\"\n");
			Files["https://pack.example/" + relative] = served;
		}

		public Task Sync()
		{
			string pack = "name = \"NORCOM\"\nversion = \"1.0.38\"\n[index]\nfile = \"index.toml\"\nhash-format = \"sha256\"\nhash = \"" + PackHash + "\"\n[versions]\nminecraft = \"26.3\"\nfabric = \"0.20.1\"\n";
			Files["https://pack.example/pack.toml"] = Encoding.UTF8.GetBytes(pack);
			return new PackSyncService(_http).SyncAsync(Game, Managed, Cache, "https://pack.example/pack.toml", new TransferMeter(), CancellationToken.None);
		}

		public void Dispose()
		{
			_http.Dispose();
			Directory.Delete(Root, true);
		}

		private sealed class Handler : HttpMessageHandler
		{
			public Dictionary<string, byte[]> Files { get; } = new Dictionary<string, byte[]>(StringComparer.Ordinal);

			protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			{
				string url = request.RequestUri?.AbsoluteUri ?? "";
				int query = url.IndexOf('?');
				if (query >= 0)
				{
					url = url.Substring(0, query);
				}
				Files["seen:" + url] = Array.Empty<byte>();
				if (!url.StartsWith("seen:", StringComparison.Ordinal) && Files.TryGetValue(url, out byte[]? body))
				{
					return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new ByteArrayContent(body)
					});
				}
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
			}
		}
	}
}
