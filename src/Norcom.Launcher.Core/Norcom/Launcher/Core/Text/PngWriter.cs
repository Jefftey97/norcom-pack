using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Norcom.Launcher.Core.Text;

public static class PngWriter
{
	private static readonly uint[] Table = Build();

	public static byte[] EncodeRgba(int width, int height, ReadOnlySpan<byte> rgba)
	{
		if (rgba.Length < width * height * 4)
		{
			throw new ArgumentException("RGBA buffer is too small.", "rgba");
		}
		using MemoryStream memoryStream = new MemoryStream();
		int num = width * 4;
		for (int i = 0; i < height; i++)
		{
			memoryStream.WriteByte(0);
			memoryStream.Write(rgba.Slice(i * num, num));
		}
		byte[] data = Deflate(memoryStream.ToArray());
		using MemoryStream memoryStream2 = new MemoryStream();
		memoryStream2.Write(new byte[8] { 137, 80, 78, 71, 13, 10, 26, 10 });
		WriteChunk(memoryStream2, "IHDR", Ihdr(width, height));
		WriteChunk(memoryStream2, "IDAT", data);
		WriteChunk(memoryStream2, "IEND", Array.Empty<byte>());
		return memoryStream2.ToArray();
	}

	private static byte[] Ihdr(int width, int height)
	{
		byte[] array = new byte[13];
		WriteBe(array, 0, width);
		WriteBe(array, 4, height);
		array[8] = 8;
		array[9] = 6;
		return array;
	}

	private static byte[] Deflate(byte[] data)
	{
		using MemoryStream memoryStream = new MemoryStream();
		memoryStream.WriteByte(120);
		memoryStream.WriteByte(156);
		using (DeflateStream deflateStream = new DeflateStream(memoryStream, CompressionLevel.Fastest, leaveOpen: true))
		{
			deflateStream.Write(data);
		}
		uint num = Adler32(data);
		Span<byte> span = stackalloc byte[4];
		span[0] = (byte)(num >> 24);
		span[1] = (byte)(num >> 16);
		span[2] = (byte)(num >> 8);
		span[3] = (byte)num;
		memoryStream.Write(span);
		return memoryStream.ToArray();
	}

	private static uint Adler32(ReadOnlySpan<byte> data)
	{
		uint num = 1u;
		uint num2 = 0u;
		ReadOnlySpan<byte> readOnlySpan = data;
		for (int i = 0; i < readOnlySpan.Length; i++)
		{
			byte b = readOnlySpan[i];
			num = (num + b) % 65521;
			num2 = (num2 + num) % 65521;
		}
		return (num2 << 16) | num;
	}

	private static void WriteChunk(Stream s, string type, byte[] data)
	{
		Span<byte> span = stackalloc byte[4];
		WriteBe(span, 0, data.Length);
		s.Write(span);
		byte[] bytes = Encoding.ASCII.GetBytes(type);
		s.Write(bytes);
		s.Write(data);
		uint v = Crc32(bytes, data);
		Span<byte> span2 = stackalloc byte[4];
		WriteBe(span2, 0, (int)v);
		s.Write(span2);
	}

	private static void WriteBe(Span<byte> b, int o, int v)
	{
		b[o] = (byte)(v >> 24);
		b[o + 1] = (byte)(v >> 16);
		b[o + 2] = (byte)(v >> 8);
		b[o + 3] = (byte)v;
	}

	private static uint Crc32(byte[] a, byte[] b)
	{
		uint num = uint.MaxValue;
		byte[] array = a;
		foreach (byte b2 in array)
		{
			num = Table[(num ^ b2) & 0xFF] ^ (num >> 8);
		}
		array = b;
		foreach (byte b3 in array)
		{
			num = Table[(num ^ b3) & 0xFF] ^ (num >> 8);
		}
		return num ^ 0xFFFFFFFFu;
	}

	private static uint[] Build()
	{
		uint[] array = new uint[256];
		for (uint num = 0u; num < 256; num++)
		{
			uint num2 = num;
			for (int i = 0; i < 8; i++)
			{
				num2 = (((num2 & (true ? 1u : 0u)) != 0) ? (0xEDB88320u ^ (num2 >> 1)) : (num2 >> 1));
			}
			array[num] = num2;
		}
		return array;
	}
}
