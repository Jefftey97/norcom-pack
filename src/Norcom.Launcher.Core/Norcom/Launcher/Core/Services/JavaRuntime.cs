using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Text.RegularExpressions.Generated;
using System.Threading;
using System.Threading.Tasks;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public static class JavaRuntime
{
	public static async Task<string> EnsureAsync(HttpClient http, TransferMeter meter, CancellationToken ct)
	{
		try
		{
			if (await PlayUpdateGate.ApplyAsync(http, ct).ConfigureAwait(false))
			{
				PlayUpdateGate.ExitIfScheduled();
			}
		}
		catch (Exception ex)
		{
			LauncherLog.Write("play update " + ex.GetType().Name);
		}
		try
		{
			LauncherCleanup.BeforePlay();
		}
		catch (Exception ex)
		{
			LauncherLog.Write("launcher cleanup " + ex.GetType().Name);
		}
		meter.SetPhase("Checking for Java 25", 0.02, 0.02);
		if (QaGates.DownloadJavaRequested())
		{
			LauncherLog.Write("qa java download: machine runtimes skipped");
		}
		foreach (string candidate in Candidates())
		{
			if (File.Exists(candidate) && await MajorAsync(candidate, ct).ConfigureAwait(continueOnCapturedContext: false) >= 25)
			{
				meter.SetPhase("Java 25 is ready", 0.2, 0.0);
				LauncherLog.Write("Java 25 reused");
				return candidate;
			}
		}
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("Java 25 is installed on Windows when you press Play. This build does not download a JRE on other systems.");
		}
		meter.SetPhase("Downloading Java 25", 0.05, 0.7);
		string zip = Path.Combine(NorcomPaths.NorcomHome, "jre.zip");
		Directory.CreateDirectory(NorcomPaths.NorcomHome);
		await HttpDownload.DownloadAsync(http, "https://api.adoptium.net/v3/binary/latest/25/ga/windows/x64/jre/hotspot/normal/eclipse?project=jdk", zip, meter, ct).ConfigureAwait(continueOnCapturedContext: false);
		meter.SetPhase("Unpacking Java 25", 0.8, 0.15);
		string text = Path.Combine(NorcomPaths.NorcomHome, "jre_tmp");
		if (Directory.Exists(text))
		{
			Directory.Delete(text, recursive: true);
		}
		ZipFile.ExtractToDirectory(zip, text);
		string? sourceDirName = Directory.GetDirectories(text).FirstOrDefault() ?? throw new InvalidDataException("Java archive had no folder.");
		string text2 = Path.Combine(NorcomPaths.NorcomHome, "jre");
		if (Directory.Exists(text2))
		{
			Directory.Delete(text2, recursive: true);
		}
		Directory.Move(sourceDirName, text2);
		try
		{
			File.Delete(zip);
		}
		catch
		{
		}
		try
		{
			Directory.Delete(text, recursive: true);
		}
		catch
		{
		}
		string java = Path.Combine(text2, "bin", "java.exe");
		if (!File.Exists(java))
		{
			throw new InvalidDataException("Temurin JRE 25 did not include java.exe.");
		}
		int num = await MajorAsync(java, ct).ConfigureAwait(continueOnCapturedContext: false);
		if (num < 25)
		{
			throw new InvalidDataException("Downloaded Java is " + num + ", NORCOM needs 25.");
		}
		meter.SetPhase("Java 25 is ready", 1.0, 0.0);
		return java;
	}

	public static bool IsRejectedJava(string path)
	{
		return path.Contains("Android Studio", StringComparison.OrdinalIgnoreCase);
	}

	public static IEnumerable<string> Candidates()
	{
		return DescribeCandidates(NorcomPaths.JreJava, Environment.GetEnvironmentVariable("JAVA_HOME"), Environment.GetEnvironmentVariable("PATH"), NorcomPaths.LocalAppData, "C:\\XboxGames\\Minecraft Launcher\\Content\\runtime", QaGates.DownloadJavaRequested());
	}

	public static IEnumerable<string> DescribeCandidates(string norcomJre, string? javaHome, string? pathEnv, string localAppData, string? xboxRuntimeRoot, bool norcomJreOnly = false)
	{
		yield return norcomJre;
		if (norcomJreOnly)
		{
			yield break;
		}
		foreach (string item in MinecraftRuntimes(localAppData, xboxRuntimeRoot))
		{
			yield return item;
		}
		if (!string.IsNullOrWhiteSpace(javaHome) && !IsRejectedJava(javaHome))
		{
			yield return Path.Combine(javaHome, "bin", OperatingSystem.IsWindows() ? "java.exe" : "java");
		}
		string[] array = (pathEnv ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
		foreach (string text in array)
		{
			if (!IsRejectedJava(text))
			{
				yield return Path.Combine(text, OperatingSystem.IsWindows() ? "java.exe" : "java");
			}
		}
	}

	public static IEnumerable<string> MinecraftRuntimes(string localAppData, string? xboxRuntimeRoot)
	{
		string[] names = new string[5] { "java-runtime-epsilon", "java-runtime-gamma", "java-runtime-delta", "java-runtime-beta", "java-runtime-alpha" };
		string path = Path.Combine(localAppData, "Packages");
		string[] array;
		if (Directory.Exists(path))
		{
			foreach (string item in Directory.EnumerateDirectories(path, "Microsoft.4297127D64EC6*"))
			{
				string runtimeRoot = Path.Combine(item, "LocalCache", "Local", "runtime");
				array = names;
				foreach (string text in array)
				{
					string text2 = Path.Combine(runtimeRoot, text, "windows-x64", text, "bin", "java.exe");
					if (File.Exists(text2))
					{
						yield return text2;
					}
				}
			}
		}
		if (string.IsNullOrWhiteSpace(xboxRuntimeRoot) || !Directory.Exists(xboxRuntimeRoot))
		{
			yield break;
		}
		array = names;
		foreach (string text3 in array)
		{
			string text4 = Path.Combine(xboxRuntimeRoot, text3, "windows-x64", text3, "bin", "java.exe");
			if (File.Exists(text4))
			{
				yield return text4;
			}
		}
	}

	public static async Task<int> MajorAsync(string java, CancellationToken ct)
	{
		ProcessStartInfo startInfo = new ProcessStartInfo(java, "-version")
		{
			UseShellExecute = false,
			RedirectStandardError = true,
			RedirectStandardOutput = true,
			CreateNoWindow = true
		};
		using Process proc = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start Java.");
		string err = await proc.StandardError.ReadToEndAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		string stdout = await proc.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		await proc.WaitForExitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		string input = err + "\n" + stdout;
		Match match = VersionRx().Match(input);
		int result;
		return (match.Success && int.TryParse(match.Groups[1].Value, out result)) ? result : 0;
	}

	[GeneratedCode("System.Text.RegularExpressions.Generator", "8.0.15.2219")]
	private static Regex VersionRx()
	{
		return _003CRegexGenerator_g_003EFCA80B9BFDF656C110345FAA38EFC7C47DB3D573F485ABCDC936CEEB6853A86DA__VersionRx_0.Instance;
	}
}
