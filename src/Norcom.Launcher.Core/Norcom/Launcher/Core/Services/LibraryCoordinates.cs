using System.Text.Json;

namespace Norcom.Launcher.Core.Services;

public static class LibraryCoordinates
{
	public static bool TryResolve(JsonElement lib, out LibraryFile file)
	{
		file = null;
		JsonElement value;
		string text = (lib.TryGetProperty("name", out value) ? (value.GetString() ?? "") : "");
		if (lib.TryGetProperty("downloads", out var value2) && value2.TryGetProperty("artifact", out var value3))
		{
			string text2 = value3.GetProperty("path").GetString() ?? "";
			string text3 = value3.GetProperty("url").GetString() ?? "";
			JsonElement value4;
			string sha = (value3.TryGetProperty("sha1", out value4) ? value4.GetString() : null);
			file = new LibraryFile(text, text2, text3, sha);
			if (text2.Length > 0)
			{
				return text3.Length > 0;
			}
			return false;
		}
		if (!TryMavenPath(text, out string path))
		{
			return false;
		}
		JsonElement value5;
		string text4 = (lib.TryGetProperty("url", out value5) ? (value5.GetString() ?? "https://libraries.minecraft.net/") : "https://libraries.minecraft.net/");
		if (!text4.EndsWith('/'))
		{
			text4 += "/";
		}
		JsonElement value6;
		string sha2 = (lib.TryGetProperty("sha1", out value6) ? value6.GetString() : null);
		file = new LibraryFile(text, path, text4 + path, sha2);
		return true;
	}

	public static bool TryMavenPath(string coordinate, out string path)
	{
		path = "";
		string[] array = coordinate.Split(':');
		if (array.Length < 3)
		{
			return false;
		}
		string value = array[0].Replace('.', '/');
		string text = array[1];
		string text2 = array[2];
		string text3 = ((array.Length >= 4) ? array[3] : "");
		string value2 = ((text3.Length == 0) ? (text + "-" + text2 + ".jar") : $"{text}-{text2}-{text3}.jar");
		path = $"{value}/{text}/{text2}/{value2}";
		return true;
	}
}
