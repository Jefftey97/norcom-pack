using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public static class HttpDownload
{
	// A quiet body is cancelled. Bytes that keep arriving extend the same budget, so a large file on a slow link can finish.
	public static TimeSpan FileDeadline { get; internal set; } = TimeSpan.FromSeconds(60);

	public static Task DownloadAsync(HttpClient http, string url, string dest, TransferMeter meter, CancellationToken ct)
	{
		return DownloadAsync(http, url, dest, meter, ct, null);
	}

	// beforeLocalWrite runs immediately before the parent directory is created, before the .part file is created, and before that partial is moved onto dest.
	public static async Task DownloadAsync(HttpClient http, string url, string dest, TransferMeter meter, CancellationToken ct, Action beforeLocalWrite)
	{
		string name = Path.GetFileName(dest);
		LauncherLog.Write("download " + name);
		string tmp = dest + ".part";
		using CancellationTokenSource budget = OpenBudget(ct);
		try
		{
			beforeLocalWrite?.Invoke();
			Directory.CreateDirectory(Path.GetDirectoryName(dest));
			using HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, budget.Token).ConfigureAwait(continueOnCapturedContext: false);
			response.EnsureSuccessStatusCode();
			KeepAlive(budget);
			long? total = response.Content.Headers.ContentLength;
			Stream input = await response.Content.ReadAsStreamAsync(budget.Token).ConfigureAwait(continueOnCapturedContext: false);
			try
			{
				beforeLocalWrite?.Invoke();
				FileStream output = File.Create(tmp);
				try
				{
					byte[] buffer = new byte[131072];
					while (true)
					{
						int i = await input.ReadAsync(buffer, budget.Token).ConfigureAwait(continueOnCapturedContext: false);
						if (i == 0)
						{
							break;
						}
						KeepAlive(budget);
						await output.WriteAsync(buffer.AsMemory(0, i), budget.Token).ConfigureAwait(continueOnCapturedContext: false);
						meter.Add(i, total);
					}
					await output.DisposeAsync().ConfigureAwait(continueOnCapturedContext: false);
					beforeLocalWrite?.Invoke();
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
		catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
		{
			DeleteQuiet(tmp);
			throw new TimeoutException("Download timed out for " + name + ".", ex);
		}
		catch
		{
			DeleteQuiet(tmp);
			throw;
		}
	}

	internal static CancellationTokenSource OpenBudget(CancellationToken ct)
	{
		CancellationTokenSource budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
		budget.CancelAfter(FileDeadline);
		return budget;
	}

	internal static void KeepAlive(CancellationTokenSource budget)
	{
		if (!budget.IsCancellationRequested)
		{
			budget.CancelAfter(FileDeadline);
		}
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
