using System;
using System.IO;

namespace Norcom.Launcher.Core.Text;

public static class LauncherLog
{
	private static readonly object Gate = new object();

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
				File.AppendAllText(logPath, text2 + Environment.NewLine);
			}
		}
		catch
		{
		}
	}
}
