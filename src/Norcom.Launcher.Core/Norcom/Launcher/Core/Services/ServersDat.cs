using System;
using System.IO;
using System.Text;

namespace Norcom.Launcher.Core.Services;

public static class ServersDat
{
	public static void Ensure(string gameDir, string name, string host)
	{
		string path = Path.Combine(gameDir, "servers.dat");
		Directory.CreateDirectory(gameDir);
		if (File.Exists(path))
		{
			Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains(host, StringComparison.OrdinalIgnoreCase);
			return;
		}
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
