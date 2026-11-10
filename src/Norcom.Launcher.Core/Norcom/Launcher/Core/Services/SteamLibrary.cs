using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace Norcom.Launcher.Core.Services;

public sealed class SteamGameLocation
{
	public bool Found { get; init; }

	public string SteamRoot { get; init; } = "";

	public string LibraryPath { get; init; } = "";

	public string GamePath { get; init; } = "";

	public string Message { get; init; } = "";
}

public sealed class SteamSources
{
	public IReadOnlyList<string> RegistryPaths { get; init; } = Array.Empty<string>();

	public Func<string, bool> FileExists { get; init; } = File.Exists;

	public Func<string, bool> DirectoryExists { get; init; } = Directory.Exists;

	public Func<string, string?> ReadText { get; init; } = ReadIfExists;

	public static SteamSources Detect()
	{
		return new SteamSources
		{
			RegistryPaths = SteamRegistry.InstallPaths()
		};
	}

	private static string? ReadIfExists(string path)
	{
		try
		{
			return File.Exists(path) ? File.ReadAllText(path) : null;
		}
		catch (IOException)
		{
			return null;
		}
	}
}

public static class SteamLibrary
{
	public const int ZomboidAppId = 108600;

	public const string NotFound = "Steam / Project Zomboid not found";

	public static SteamGameLocation FindZomboid(SteamSources sources)
	{
		List<string> roots = new List<string>();
		IReadOnlyList<string> registry = sources.RegistryPaths ?? Array.Empty<string>();
		for (int i = 0; i < registry.Count; i++)
		{
			Add(roots, registry[i]);
		}
		if (roots.Count == 0)
		{
			return Missing();
		}
		List<string> libraries = new List<string>();
		for (int i = 0; i < roots.Count; i++)
		{
			Add(libraries, roots[i]);
			string vdf = ReadVdf(sources, roots[i]);
			if (string.IsNullOrWhiteSpace(vdf))
			{
				continue;
			}
			IReadOnlyList<string> paths = LibraryPaths(vdf);
			for (int p = 0; p < paths.Count; p++)
			{
				Add(libraries, paths[p]);
			}
		}
		for (int i = 0; i < libraries.Count; i++)
		{
			string manifest = Join(libraries[i], "steamapps", "appmanifest_" + ZomboidAppId + ".acf");
			string game = Join(libraries[i], "steamapps", "common", "ProjectZomboid");
			bool manifestExists = SafeExists(sources.FileExists, manifest);
			bool gameExists = SafeExists(sources.DirectoryExists, game);
			if (!manifestExists && !gameExists)
			{
				continue;
			}
			return new SteamGameLocation
			{
				Found = true,
				SteamRoot = roots[0],
				LibraryPath = libraries[i],
				GamePath = gameExists ? game : "",
				Message = ""
			};
		}
		return Missing();
	}

	public static IReadOnlyList<string> LibraryPaths(string vdf)
	{
		List<string> paths = new List<string>();
		if (string.IsNullOrEmpty(vdf))
		{
			return paths;
		}
		bool nextIsPath = false;
		foreach (string token in Quoted(vdf))
		{
			if (token.Equals("path", StringComparison.OrdinalIgnoreCase))
			{
				nextIsPath = true;
				continue;
			}
			if (!nextIsPath)
			{
				continue;
			}
			nextIsPath = false;
			if (token.Length > 0)
			{
				paths.Add(token);
			}
		}
		return paths;
	}

	public static string Join(string root, params string[] parts)
	{
		bool windows = root.IndexOf('\\') >= 0;
		char sep = windows ? '\\' : '/';
		StringBuilder builder = new StringBuilder(root.TrimEnd('\\', '/'));
		for (int i = 0; i < parts.Length; i++)
		{
			builder.Append(sep);
			builder.Append(parts[i].Trim('\\', '/'));
		}
		return builder.ToString();
	}

	private static string ReadVdf(SteamSources sources, string steamRoot)
	{
		string primary = Join(steamRoot, "steamapps", "libraryfolders.vdf");
		string text = sources.ReadText(primary);
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		return sources.ReadText(Join(steamRoot, "config", "libraryfolders.vdf")) ?? "";
	}

	private static IEnumerable<string> Quoted(string text)
	{
		for (int i = 0; i < text.Length; i++)
		{
			if (text[i] != '"')
			{
				continue;
			}
			StringBuilder token = new StringBuilder();
			for (i++; i < text.Length; i++)
			{
				char c = text[i];
				if (c == '\\' && i + 1 < text.Length)
				{
					token.Append(text[i + 1]);
					i++;
					continue;
				}
				if (c == '"')
				{
					break;
				}
				token.Append(c);
			}
			yield return token.ToString();
		}
	}

	private static void Add(List<string> paths, string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}
		string trimmed = path.Trim().TrimEnd('\\', '/');
		for (int i = 0; i < paths.Count; i++)
		{
			if (string.Equals(paths[i], trimmed, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
		}
		paths.Add(trimmed);
	}

	private static bool SafeExists(Func<string, bool> probe, string path)
	{
		try
		{
			return probe(path);
		}
		catch (Exception)
		{
			return false;
		}
	}

	private static SteamGameLocation Missing()
	{
		return new SteamGameLocation
		{
			Found = false,
			Message = NotFound
		};
	}
}

public static class SteamRegistry
{
	public static IReadOnlyList<string> InstallPaths()
	{
		List<string> paths = new List<string>();
		Add(paths, RegistryHive.CurrentUser, RegistryView.Default, "Software\\Valve\\Steam", "SteamPath");
		Add(paths, RegistryHive.LocalMachine, RegistryView.Registry64, "SOFTWARE\\WOW6432Node\\Valve\\Steam", "InstallPath");
		Add(paths, RegistryHive.LocalMachine, RegistryView.Registry32, "SOFTWARE\\Valve\\Steam", "InstallPath");
		Add(paths, RegistryHive.LocalMachine, RegistryView.Registry64, "SOFTWARE\\Valve\\Steam", "InstallPath");
		return paths;
	}

	private static void Add(List<string> paths, RegistryHive hive, RegistryView view, string subkey, string name)
	{
		try
		{
			using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
			using RegistryKey key = baseKey.OpenSubKey(subkey);
			string value = key?.GetValue(name) as string;
			if (string.IsNullOrWhiteSpace(value))
			{
				return;
			}
			string trimmed = value.Trim().TrimEnd('\\', '/');
			for (int i = 0; i < paths.Count; i++)
			{
				if (string.Equals(paths[i], trimmed, StringComparison.OrdinalIgnoreCase))
				{
					return;
				}
			}
			paths.Add(trimmed);
		}
		catch (Exception)
		{
		}
	}
}
