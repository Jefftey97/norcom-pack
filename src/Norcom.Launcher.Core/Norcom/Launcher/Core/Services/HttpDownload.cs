using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Norcom.Launcher.Core.Services;

public static class HttpDownload
{
	public static async Task DownloadAsync(HttpClient http, string url, string dest, TransferMeter meter, CancellationToken ct)
	{
		string tmp = dest + ".part";
		Directory.CreateDirectory(Path.GetDirectoryName(dest));
		using HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(continueOnCapturedContext: false);
		response.EnsureSuccessStatusCode();
		long? total = response.Content.Headers.ContentLength;
		Stream input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			FileStream output = File.Create(tmp);
			try
			{
				byte[] buffer = new byte[131072];
				while (true)
				{
					int i = await input.ReadAsync(buffer, ct).ConfigureAwait(continueOnCapturedContext: false);
					if (i == 0)
					{
						break;
					}
					await output.WriteAsync(buffer.AsMemory(0, i), ct).ConfigureAwait(continueOnCapturedContext: false);
					meter.Add(i, total);
				}
				await output.DisposeAsync().ConfigureAwait(continueOnCapturedContext: false);
				File.Move(tmp, dest, overwrite: true);
			}
			finally
			{
				if (output != null)
				{
					await output.DisposeAsync();
				}
			}
		}
		finally
		{
			if (input != null)
			{
				await input.DisposeAsync();
			}
		}
	}
}
