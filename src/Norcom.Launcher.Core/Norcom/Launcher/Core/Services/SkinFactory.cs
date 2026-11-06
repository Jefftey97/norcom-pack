using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public static class SkinFactory
{
	public static byte[] Steve()
	{
		return Paint(slim: false, Rgb(58, 36, 22), Rgb(58, 78, 196), Rgb(46, 46, 138));
	}

	public static byte[] Alex()
	{
		return Paint(slim: true, Rgb(227, 139, 58), Rgb(58, 138, 74), Rgb(58, 74, 48));
	}

	private static byte[] Paint(bool slim, (byte r, byte g, byte b) hair, (byte r, byte g, byte b) shirt, (byte r, byte g, byte b) pants)
	{
		(byte, byte, byte) c2 = Rgb(196, 138, 98);
		byte[] px = new byte[16384];
		Fill(8, 0, 8, 8, hair);
		Fill(16, 0, 8, 8, hair);
		Fill(0, 8, 8, 8, hair);
		Fill(8, 8, 8, 8, c2);
		Fill(16, 8, 8, 8, hair);
		Fill(24, 8, 8, 8, hair);
		Fill(10, 12, 2, 2, Rgb(byte.MaxValue, byte.MaxValue, byte.MaxValue));
		Fill(14, 12, 2, 2, Rgb(byte.MaxValue, byte.MaxValue, byte.MaxValue));
		Fill(11, 13, 1, 1, Rgb(60, 90, 160));
		Fill(15, 13, 1, 1, Rgb(60, 90, 160));
		Fill(12, 15, 2, 1, Rgb(140, 70, 70));
		Fill(20, 20, 8, 12, shirt);
		Fill(16, 20, 4, 12, shirt);
		Fill(28, 20, 4, 12, shirt);
		Fill(32, 20, 8, 12, shirt);
		Fill(20, 16, 8, 4, shirt);
		if (!slim)
		{
			Fill(44, 20, 4, 12, c2);
			Fill(40, 20, 4, 12, c2);
			Fill(48, 20, 4, 12, c2);
			Fill(52, 20, 4, 12, c2);
			Fill(36, 52, 4, 12, c2);
			Fill(32, 52, 4, 12, c2);
			Fill(40, 52, 4, 12, c2);
			Fill(44, 52, 4, 12, c2);
		}
		else
		{
			Fill(44, 20, 3, 12, c2);
			Fill(40, 20, 4, 12, c2);
			Fill(47, 20, 4, 12, c2);
			Fill(51, 20, 3, 12, c2);
			Fill(36, 52, 3, 12, c2);
			Fill(32, 52, 4, 12, c2);
			Fill(39, 52, 4, 12, c2);
			Fill(43, 52, 3, 12, c2);
		}
		Fill(4, 20, 4, 12, pants);
		Fill(0, 20, 4, 12, pants);
		Fill(8, 20, 4, 12, pants);
		Fill(12, 20, 4, 12, pants);
		Fill(20, 52, 4, 12, pants);
		Fill(16, 52, 4, 12, pants);
		Fill(24, 52, 4, 12, pants);
		Fill(28, 52, 4, 12, pants);
		return PngWriter.EncodeRgba(64, 64, px);
		void Fill(int x, int y, int w, int h, (byte r, byte g, byte b) c)
		{
			for (int i = y; i < y + h; i++)
			{
				for (int j = x; j < x + w; j++)
				{
					int num = (i * 64 + j) * 4;
					px[num] = c.r;
					px[num + 1] = c.g;
					px[num + 2] = c.b;
					px[num + 3] = byte.MaxValue;
				}
			}
		}
	}

	private static (byte r, byte g, byte b) Rgb(byte r, byte g, byte b)
	{
		return (r: r, g: g, b: b);
	}
}
