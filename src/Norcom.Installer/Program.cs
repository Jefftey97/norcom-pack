using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Norcom.Launcher.Core.Services;
using Norcom.Launcher.Core.Text;
using Norcom.Launcher.Core.Updates;

internal static class Program
{
	private const string Title = "NORCOM Launcher setup";

	[STAThread]
	private static int Main()
	{
		try
		{
			string source = ExtractBundledLauncher();
			if (!CloseRunningLauncher(FriendInstaller.InstalledExePath))
			{
				Show("NORCOM Launcher is still open. Close it (and any game it started), then run Install-NORCOM.exe again. Nothing was changed.", 0x30);
				return 2;
			}
			Directory.CreateDirectory(FriendInstaller.InstallDirectory);
			UpdateStore store = new UpdateStore(FriendInstaller.InstallDirectory, ReleaseTrust.CreateVerifier());
			using (AcquireWithRetry(store, TimeSpan.FromSeconds(20)))
			{
				FriendInstaller.InstallLauncher(source, FriendInstaller.InstallDirectory);
				try
				{
					// The stored recovery launcher hash must follow the exe we just installed,
					// or every later start stops with "could not open safely".
					store.RefreshBootstrapHash();
				}
				catch (Exception ex)
				{
					LauncherLog.Write("installer bootstrap refresh " + ex.GetType().Name);
				}
			}
			FriendInstaller.PublishShortcuts(FriendInstaller.InstalledExePath, createIfMissing: true);
			Process.Start(new ProcessStartInfo(FriendInstaller.InstalledExePath)
			{
				UseShellExecute = true
			});
			return 0;
		}
		catch (Exception ex)
		{
			LauncherLog.Write("installer " + ex.GetType().Name);
			Show("NORCOM install failed: " + ex.Message, 0x10);
			return 1;
		}
	}

	private static IDisposable AcquireWithRetry(UpdateStore store, TimeSpan budget)
	{
		Stopwatch clock = Stopwatch.StartNew();
		while (true)
		{
			try
			{
				return store.Acquire();
			}
			catch (IOException) when (clock.Elapsed < budget)
			{
				Thread.Sleep(500);
			}
		}
	}

	private static bool CloseRunningLauncher(string installedExe)
	{
		string target = Path.GetFullPath(installedExe);
		string appRoot = Path.GetDirectoryName(target) ?? target;
		for (int round = 0; round < 2; round++)
		{
			Process[] found = Process.GetProcessesByName("NorcomLauncher");
			bool any = false;
			try
			{
				foreach (Process process in found)
				{
					string path;
					try
					{
						path = process.MainModule?.FileName ?? "";
					}
					catch
					{
						path = "";
					}
					if (path.Length > 0 && !path.StartsWith(appRoot, StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
					any = true;
					if (round == 0)
					{
						try
						{
							process.CloseMainWindow();
						}
						catch
						{
						}
					}
				}
				if (!any)
				{
					return true;
				}
				if (round == 0)
				{
					foreach (Process process in found)
					{
						try
						{
							process.WaitForExit(15000);
						}
						catch
						{
						}
					}
				}
			}
			finally
			{
				foreach (Process process in found)
				{
					process.Dispose();
				}
			}
		}
		return false;
	}

	private static void Show(string text, uint icon)
	{
		try
		{
			MessageBoxW(IntPtr.Zero, text, Title, icon);
		}
		catch
		{
			Console.Error.WriteLine(text);
		}
	}

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

	private static string ExtractBundledLauncher()
	{
		string beside = Path.Combine(AppContext.BaseDirectory, "NorcomLauncher.exe");
		if (File.Exists(beside))
		{
			return beside;
		}
		Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("NorcomLauncher.exe");
		if (stream == null)
		{
			throw new FileNotFoundException("Put NorcomLauncher.exe next to Install-NORCOM.exe.");
		}
		using (stream)
		{
			string dir = Path.Combine(Path.GetTempPath(), "norcom-install");
			Directory.CreateDirectory(dir);
			string dest = Path.Combine(dir, "NorcomLauncher.exe");
			using FileStream file = File.Create(dest);
			stream.CopyTo(file);
			return dest;
		}
	}
}
