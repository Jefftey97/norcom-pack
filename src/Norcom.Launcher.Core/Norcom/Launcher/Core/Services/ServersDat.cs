using System;
using System.IO;
using System.Text;
using Norcom.Launcher.Core;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public static class ServersDat
{
	public static void Ensure(string gameDir, string name, string host)
	{
		string path = Path.Combine(gameDir, "servers.dat");
		Directory.CreateDirectory(gameDir);
		if (File.Exists(path))
		{
			RefreshListedPort(path, FriendsServer.IsHostPc() ? null : FriendsServer.Resolve());
			return;
		}
		host = ListedAddress(host);
		using MemoryStream memoryStream = new MemoryStream();
		memoryStream.WriteByte(10);
		WriteString(memoryStream, "");
		memoryStream.WriteByte(9);
		WriteString(memoryStream, "servers");
		memoryStream.WriteByte(10);
		WriteInt(memoryStream, 1);
		memoryStream.WriteByte(8);
		WriteString(memoryStream, "name");
		WriteString(memoryStream, name);
		memoryStream.WriteByte(8);
		WriteString(memoryStream, "ip");
		WriteString(memoryStream, host);
		memoryStream.WriteByte(1);
		WriteString(memoryStream, "hidden");
		memoryStream.WriteByte(0);
		memoryStream.WriteByte(0);
		memoryStream.WriteByte(0);
		File.WriteAllBytes(path, memoryStream.ToArray());
	}

	/// <summary>
	/// Friends keep their servers.dat between launches, so an entry written as host:port goes stale
	/// when playit moves the tunnel. When a live SRV answer is in hand, rewrite any
	/// "shrimp-cub.tun.ply.gg:&lt;port&gt;" string to the current port. NBT strings are
	/// length-prefixed and the surrounding list/compound tags carry no byte lengths, so swapping one
	/// string (and its 2-byte length) in place keeps the file valid and leaves other servers alone.
	/// </summary>
	public static bool RefreshListedPort(string path, FriendsEndpoint? endpoint)
	{
		if (endpoint == null || endpoint.Value.Kind != "srv")
		{
			return false;
		}
		try
		{
			byte[] data = File.ReadAllBytes(path);
			byte[] prefix = Encoding.UTF8.GetBytes(NorcomPaths.ServerHost + ":");
			byte[] wanted = Encoding.UTF8.GetBytes(endpoint.Value.Display);
			using MemoryStream output = new MemoryStream(data.Length + 8);
			bool changed = false;
			int i = 0;
			while (i < data.Length)
			{
				if (i >= 2 && Matches(data, i, prefix))
				{
					int end = i + prefix.Length;
					while (end < data.Length && data[end] >= (byte)'0' && data[end] <= (byte)'9')
					{
						end++;
					}
					int declared = (data[i - 2] << 8) | data[i - 1];
					if (end > i + prefix.Length && declared == end - i && !Same(data, i, end - i, wanted))
					{
						output.SetLength(output.Length - 2);
						output.Position = output.Length;
						output.WriteByte((byte)(wanted.Length >> 8));
						output.WriteByte((byte)wanted.Length);
						output.Write(wanted);
						i = end;
						changed = true;
						continue;
					}
				}
				output.WriteByte(data[i]);
				i++;
			}
			if (!changed)
			{
				return false;
			}
			string temp = path + ".norcom-tmp";
			File.WriteAllBytes(temp, output.ToArray());
			File.Copy(temp, path, overwrite: true);
			File.Delete(temp);
			LauncherLog.Write("servers.dat port refreshed to " + endpoint.Value.Display);
			return true;
		}
		catch (Exception ex)
		{
			LauncherLog.Write("servers.dat refresh " + ex.GetType().Name);
			return false;
		}
	}

	private static bool Matches(byte[] data, int at, byte[] text)
	{
		if (at + text.Length > data.Length)
		{
			return false;
		}
		for (int k = 0; k < text.Length; k++)
		{
			if (data[at + k] != text[k])
			{
				return false;
			}
		}
		return true;
	}

	private static bool Same(byte[] data, int at, int length, byte[] text)
	{
		return length == text.Length && Matches(data, at, text);
	}

	private static string ListedAddress(string host)
	{
		if (string.IsNullOrWhiteSpace(host) || host.Contains(NorcomPaths.ServerHost, StringComparison.OrdinalIgnoreCase))
		{
			return FriendsServer.Resolve().Display;
		}
		return host.Trim();
	}

	private static void WriteString(Stream s, string value)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(value);
		s.WriteByte((byte)(bytes.Length >> 8));
		s.WriteByte((byte)bytes.Length);
		s.Write(bytes);
	}

	private static void WriteInt(Stream s, int v)
	{
		s.WriteByte((byte)(v >> 24));
		s.WriteByte((byte)(v >> 16));
		s.WriteByte((byte)(v >> 8));
		s.WriteByte((byte)v);
	}
}
