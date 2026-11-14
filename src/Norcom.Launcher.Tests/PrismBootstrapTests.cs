using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

public class PrismBootstrapTests
{
	private sealed class NoNetwork : HttpMessageHandler
	{
		public int Calls;

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Calls++;
			throw new HttpRequestException("offline");
		}
	}

	private static string NewDir()
	{
		string dir = Path.Combine(Path.GetTempPath(), "norcom-prism-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(dir);
		return dir;
	}

	private static string MakeZip(string dir, params string[] names)
	{
		string zip = Path.Combine(dir, "prism.zip");
		using ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create);
		foreach (string name in names)
		{
			using StreamWriter writer = new StreamWriter(archive.CreateEntry(name).Open());
			writer.Write("x");
		}
		return zip;
	}

	[Fact]
	public void PinnedReleaseIsTheOfficialNonPortableBuild()
	{
		Assert.StartsWith("https://github.com/PrismLauncher/PrismLauncher/releases/download/" + PrismBootstrap.Version + "/", PrismBootstrap.ArchiveUrl);
		Assert.DoesNotContain("Portable", PrismBootstrap.ArchiveUrl);
		Assert.Equal(64, PrismBootstrap.ArchiveSha256.Length);
	}

	[Fact]
	public void InstallFolderIsWhereTheLauncherLooksFirst()
	{
		string local = NewDir();
		string exe = Path.Combine(PrismBootstrap.InstallDir(local), PrismBootstrap.ExeName);
		Assert.Equal(exe, PrismInstall.Candidates(local, Path.Combine(local, "pf")).First(), ignoreCase: true);
	}

	[Fact]
	public async Task ExistingPrismIsLeftAloneAndNothingIsDownloaded()
	{
		string local = NewDir();
		string exe = Path.Combine(local, "Prism Launcher", "prismlauncher.exe");
		Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
		File.WriteAllText(exe, "mine");
		NoNetwork handler = new NoNetwork();
		using HttpClient http = new HttpClient(handler);
		bool installed = await PrismBootstrap.EnsureAsync(http, new TransferMeter(), null, new[] { exe }, local, NewDir(), CancellationToken.None);
		Assert.False(installed);
		Assert.Equal(0, handler.Calls);
		Assert.Equal("mine", File.ReadAllText(exe));
		bool overridden = await PrismBootstrap.EnsureAsync(http, new TransferMeter(), @"C:\custom\prismlauncher.exe", Array.Empty<string>(), local, NewDir(), CancellationToken.None);
		Assert.False(overridden);
		Assert.Equal(0, handler.Calls);
	}

	[Fact]
	public async Task FailedDownloadInstallsNothing()
	{
		string local = NewDir();
		string work = NewDir();
		using HttpClient http = new HttpClient(new NoNetwork());
		await Assert.ThrowsAnyAsync<Exception>(() => PrismBootstrap.EnsureAsync(http, new TransferMeter(), null, Array.Empty<string>(), local, work, CancellationToken.None));
		Assert.False(Directory.Exists(PrismBootstrap.InstallDir(local)));
		Assert.Empty(Directory.GetFiles(work));
	}

	[Fact]
	public void WrongSizeOrChecksumIsRefused()
	{
		string dir = NewDir();
		string zip = MakeZip(dir, "prismlauncher.exe");
		long size = new FileInfo(zip).Length;
		string sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip)));
		PrismBootstrap.Verify(zip, size, sha.ToLowerInvariant());
		Assert.Throws<InvalidDataException>(() => PrismBootstrap.Verify(zip, size + 1, sha));
		Assert.Throws<InvalidDataException>(() => PrismBootstrap.Verify(zip, size, new string('0', 64)));
	}

	[Fact]
	public void GoodArchiveMovesIntoPlaceAndBadOnesDoNot()
	{
		string dir = NewDir();
		string target = Path.Combine(dir, "Programs", "PrismLauncher");
		PrismBootstrap.Install(MakeZip(NewDir(), "prismlauncher.exe", "qt.conf"), target + ".norcom-tmp", target);
		Assert.True(File.Exists(Path.Combine(target, "prismlauncher.exe")));
		Assert.False(Directory.Exists(target + ".norcom-tmp"));

		string noExe = Path.Combine(NewDir(), "Programs", "PrismLauncher");
		Assert.Throws<InvalidDataException>(() => PrismBootstrap.Install(MakeZip(NewDir(), "readme.txt"), noExe + ".norcom-tmp", noExe));
		Assert.False(Directory.Exists(noExe));

		string portable = Path.Combine(NewDir(), "Programs", "PrismLauncher");
		Assert.Throws<InvalidDataException>(() => PrismBootstrap.Install(MakeZip(NewDir(), "prismlauncher.exe", "portable.txt"), portable + ".norcom-tmp", portable));
		Assert.False(Directory.Exists(portable));
	}
}
