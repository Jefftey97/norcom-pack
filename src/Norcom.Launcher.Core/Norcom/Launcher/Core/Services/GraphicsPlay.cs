using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Norcom.Launcher.Core.Models;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public static class GraphicsPlay
{
	public static Func<PcHardware?>? Probe { get; set; }

	public static GraphicsTier ApplyAuto(string gameDir)
	{
		PcHardware? hardware = null;
		try
		{
			hardware = (Probe ?? HardwareProbe.TryRead)();
		}
		catch (Exception ex)
		{
			LauncherLog.Write("graphics probe " + ex.GetType().Name);
		}
		string notePath = Path.Combine(gameDir, "norcom-graphics-tier.txt");
		string previous = File.Exists(notePath) ? File.ReadAllText(notePath) : "";
		bool saved3070 = previous.Contains("sticky=3070", StringComparison.OrdinalIgnoreCase);
		bool seen3070 = saved3070 || (hardware != null && MemoryPlan.Is3070Class(hardware.GpuName));
		GraphicsTier tier;
		if (seen3070 || hardware == null)
		{
			tier = GraphicsTier.High;
		}
		else
		{
			tier = GraphicsAuto.Choose(hardware);
		}
		bool healed = HealIfDrifted(gameDir);
		WriteTierNote(gameDir, tier, seen3070);
		if (!healed)
		{
			LauncherLog.Write("play graphics kept custom settings");
		}
		return tier;
	}

	public static bool HealIfDrifted(string gameDir)
	{
		Directory.CreateDirectory(gameDir);
		string optionsPath = Path.Combine(gameDir, "options.txt");
		string existing = File.Exists(optionsPath) ? File.ReadAllText(optionsPath) : "";
		string packs = ReadOption(existing, "resourcePacks");
		string healedPacks = GraphicsPack.HealPackList(packs);
		bool packsDrifted = !GraphicsPack.SamePackList(packs, healedPacks);
		bool alacrity = packs.Contains("file/Alacrity.zip", StringComparison.OrdinalIgnoreCase);
		bool overlayDrifted = alacrity && !OverlayReady(gameDir);
		bool rainDrifted = RainDrifted(gameDir);
		if (!packsDrifted && !overlayDrifted && !rainDrifted)
		{
			return false;
		}
		if (alacrity)
		{
			try
			{
				GraphicsService.HealOverlay(gameDir);
			}
			catch (Exception ex)
			{
				LauncherLog.Write("play overlay " + ex.GetType().Name);
			}
		}
		try
		{
			GraphicsService.HealMarble(gameDir);
		}
		catch (Exception ex)
		{
			LauncherLog.Write("play marble " + ex.GetType().Name);
		}
		if (packsDrifted)
		{
			Dictionary<string, string> updates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				["resourcePacks"] = healedPacks
			};
			File.WriteAllText(optionsPath, OptionsPatch.Apply(string.IsNullOrEmpty(existing) ? null : existing, updates));
			LauncherLog.Write("play graphics healed resource packs");
		}
		if (rainDrifted)
		{
			FixRain(gameDir);
		}
		return true;
	}

	private static void WriteTierNote(string gameDir, GraphicsTier tier, bool seen3070)
	{
		string text = "tier=" + tier + "\nappliedUtc=" + DateTimeOffset.UtcNow.ToString("o") + "\n";
		if (seen3070)
		{
			text += "sticky=3070\n";
		}
		string notePath = Path.Combine(gameDir, "norcom-graphics-tier.txt");
		File.WriteAllText(notePath, text);
		string config = Path.Combine(gameDir, "config");
		Directory.CreateDirectory(config);
		File.WriteAllText(Path.Combine(config, "norcom-tier.txt"), text);
	}

	private static bool OverlayReady(string gameDir)
	{
		string dest = Path.Combine(gameDir, "resourcepacks", GraphicsPack.OverlayFile);
		if (!File.Exists(dest))
		{
			return false;
		}
		string sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dest)));
		return sha.Equals(GraphicsPack.OverlaySha256, StringComparison.OrdinalIgnoreCase);
	}

	private static bool RainDrifted(string gameDir)
	{
		string[] names = new string[2] { "ComplementaryUnbound_r5.9.3.txt", "ComplementaryUnbound_r5.9.3.zip.txt" };
		for (int i = 0; i < names.Length; i++)
		{
			string path = Path.Combine(gameDir, "shaderpacks", names[i]);
			if (File.Exists(path) && ShaderScale.RainStyleDrifted(File.ReadAllText(path)))
			{
				return true;
			}
		}
		return false;
	}

	private static void FixRain(string gameDir)
	{
		string[] names = new string[2] { "ComplementaryUnbound_r5.9.3.txt", "ComplementaryUnbound_r5.9.3.zip.txt" };
		for (int i = 0; i < names.Length; i++)
		{
			string path = Path.Combine(gameDir, "shaderpacks", names[i]);
			if (!File.Exists(path))
			{
				continue;
			}
			string text = File.ReadAllText(path);
			string updated = ShaderScale.CoerceRainStyle(text);
			if (!string.Equals(text, updated, StringComparison.Ordinal))
			{
				File.WriteAllText(path, updated);
				LauncherLog.Write("play graphics healed RAIN_STYLE");
			}
		}
	}

	private static string ReadOption(string options, string key)
	{
		string[] lines = options.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
		for (int i = 0; i < lines.Length; i++)
		{
			string line = lines[i];
			if (line.StartsWith("key_", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			int split = line.IndexOf(':');
			if (split > 0 && line.Substring(0, split).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
			{
				return line.Substring(split + 1).Trim();
			}
		}
		return "";
	}
}
