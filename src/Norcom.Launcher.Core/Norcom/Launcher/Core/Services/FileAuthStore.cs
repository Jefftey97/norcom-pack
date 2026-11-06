using System;
using System.IO;

namespace Norcom.Launcher.Core.Services;

public sealed class FileAuthStore : IAuthSecretStore
{
	private readonly IDataProtector _protector;

	public string Directory { get; }

	public FileAuthStore(string directory, IDataProtector protector)
	{
		Directory = directory;
		_protector = protector;
		System.IO.Directory.CreateDirectory(directory);
	}

	public void Write(string name, byte[] plaintext)
	{
		string path = Safe(name);
		byte[] bytes = _protector.Protect(plaintext);
		File.WriteAllBytes(path, bytes);
	}

	public byte[]? Read(string name)
	{
		string path = Safe(name);
		if (!File.Exists(path))
		{
			return null;
		}
		try
		{
			return _protector.Unprotect(File.ReadAllBytes(path));
		}
		catch
		{
			try
			{
				File.Delete(path);
			}
			catch
			{
			}
			return null;
		}
	}

	public void Delete(string name)
	{
		string path = Safe(name);
		if (File.Exists(path))
		{
			File.Delete(path);
		}
	}

	private string Safe(string name)
	{
		if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(new char[3] { '/', '\\', ':' }) >= 0 || name.Contains("..", StringComparison.Ordinal))
		{
			throw new InvalidOperationException("Invalid auth file.");
		}
		ReadOnlySpan<char> readOnlySpan = Directory;
		char reference = Path.DirectorySeparatorChar;
		string fullPath = Path.GetFullPath(string.Concat(readOnlySpan, new ReadOnlySpan<char>(ref reference)));
		string fullPath2 = Path.GetFullPath(Path.Combine(Directory, name));
		if (!fullPath2.StartsWith(fullPath, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Invalid auth file.");
		}
		return fullPath2;
	}
}
