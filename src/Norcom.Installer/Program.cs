using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Norcom.Launcher.Core.Services;

internal static class Program
{
	[STAThread]
	private static int Main()
	{
		try
		{
			string source = ExtractBundledLauncher();
			FriendInstaller.InstallLauncher(source, FriendInstaller.InstallDirectory);
			Process.Start(new ProcessStartInfo(FriendInstaller.InstalledExePath)
			{
				UseShellExecute = true
			});
			return 0;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine("NORCOM install failed: " + ex.Message);
			return 1;
		}
	}

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
