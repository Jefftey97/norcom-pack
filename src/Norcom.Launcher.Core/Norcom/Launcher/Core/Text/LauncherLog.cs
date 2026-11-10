using System;
using System.IO;

namespace Norcom.Launcher.Core.Text;

public static class LauncherLog
{
	private static readonly object Gate = new object();

	// The launcher logs every minute while open; keep one previous generation
	// instead of growing the file without limit.
	public const long MaximumBytes = 4194304;

	public static void Write(string line)
	{
		string text = Redactor.Clean(line).Replace('\r', ' ').Replace('\n', ' ');
		string text2 = DateTimeOffset.Now.ToString("o") + " " + text;
		try
		{
			string logPath = NorcomPaths.LogPath;
			string directoryName = Path.GetDirectoryName(logPath);
			if (!string.IsNullOrEmpty(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			lock (Gate)
			{
				FileInfo current = new FileInfo(logPath);
				if (current.Exists && current.Length > MaximumBytes)
				{
					File.Move(logPath, logPath + ".1", overwrite: true);
				}
				File.AppendAllText(logPath, text2 + Environment.NewLine);
			}
		}
		catch
		{
		}
	}
}
