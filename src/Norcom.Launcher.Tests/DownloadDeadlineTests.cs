using System.Net;
using System.Net.Http;
using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

public class DownloadDeadlineTests
{
	[Fact]
	public async Task StalledBodyTimesOutAndLeavesTheLauncherFree()
	{
		TimeSpan previous = HttpDownload.FileDeadline;
		HttpDownload.FileDeadline = TimeSpan.FromMilliseconds(200);
		bool busy = true;
		string dir = Path.Combine(Path.GetTempPath(), "norcom-stall-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(dir);
		string dest = Path.Combine(dir, "slow.jar");
		try
		{
			using HttpClient http = new HttpClient(new StallHandler())
			{
				Timeout = Timeout.InfiniteTimeSpan
			};
			TimeoutException ex = await Assert.ThrowsAsync<TimeoutException>(() => HttpDownload.DownloadAsync(http, "https://pack.example/mods/slow.jar", dest, new TransferMeter(), CancellationToken.None));
			Assert.Contains("slow.jar", ex.Message, StringComparison.Ordinal);
			Assert.False(File.Exists(dest));
			Assert.False(File.Exists(dest + ".part"));
		}
		finally
		{
			busy = false;
			HttpDownload.FileDeadline = previous;
			if (File.Exists(dest))
			{
				File.Delete(dest);
			}
			if (File.Exists(dest + ".part"))
			{
				File.Delete(dest + ".part");
			}
			if (Directory.Exists(dir))
			{
				Directory.Delete(dir, true);
			}
		}
		Assert.False(busy);
	}

	private sealed class StallHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StreamContent(new StallStream())
			};
			return Task.FromResult(response);
		}
	}

	private sealed class StallStream : Stream
	{
		public override bool CanRead => true;

		public override bool CanSeek => false;

		public override bool CanWrite => false;

		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => 0;
			set => throw new NotSupportedException();
		}

		public override void Flush()
		{
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			throw new NotSupportedException();
		}

		public override void SetLength(long value)
		{
			throw new NotSupportedException();
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
		}

		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			await Task.Delay(Timeout.Infinite, cancellationToken);
			return 0;
		}
	}
}
