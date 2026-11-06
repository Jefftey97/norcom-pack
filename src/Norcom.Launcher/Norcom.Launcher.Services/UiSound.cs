using System;
using System.IO;
using System.Media;

namespace Norcom.Launcher.Services;

public static class UiSound
{
	public static void Play()
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Expected O, but got Unknown
		try
		{
			SoundPlayer val = new SoundPlayer((Stream)new MemoryStream(Tone(740, 70)));
			try
			{
				val.Play();
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
		catch
		{
		}
	}

	private static byte[] Tone(int frequency, int milliseconds)
	{
		int num = 22050 * milliseconds / 1000;
		byte[] array = new byte[num * 2];
		for (int i = 0; i < num; i++)
		{
			double num2 = Math.Sin(Math.PI * (double)i / (double)num);
			short num3 = (short)(Math.Sin(Math.PI * 2.0 * (double)frequency * (double)i / 22050.0) * num2 * 9000.0);
			array[i * 2] = (byte)num3;
			array[i * 2 + 1] = (byte)(num3 >> 8);
		}
		using MemoryStream memoryStream = new MemoryStream();
		using BinaryWriter binaryWriter = new BinaryWriter(memoryStream);
		binaryWriter.Write("RIFF"u8);
		binaryWriter.Write(36 + array.Length);
		binaryWriter.Write("WAVE"u8);
		binaryWriter.Write("fmt "u8);
		binaryWriter.Write(16);
		binaryWriter.Write((short)1);
		binaryWriter.Write((short)1);
		binaryWriter.Write(22050);
		binaryWriter.Write(44100);
		binaryWriter.Write((short)2);
		binaryWriter.Write((short)16);
		binaryWriter.Write("data"u8);
		binaryWriter.Write(array.Length);
		binaryWriter.Write(array);
		return memoryStream.ToArray();
	}
}
