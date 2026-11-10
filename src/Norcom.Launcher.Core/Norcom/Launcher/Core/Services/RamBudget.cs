using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Services;

public sealed class RamBudget
{
	public const int StepMb = 512;

	public const int PreferredFloorMb = 2048;

	public const int ReserveFloorMb = 4096;

	public bool Known { get; init; }

	public int TotalMb { get; init; }

	public int AvailableMb { get; init; }

	public int ReserveMb { get; init; }

	public int SafeMaxMb { get; init; }

	public int FloorMb { get; init; }

	public int RecommendedMb { get; init; }

	public int ChosenMb { get; init; }

	public string Warning { get; init; } = "";

	public string Caption { get; init; } = "";

	public static RamBudget Create(long totalBytes, long availableBytes, int requestedMb)
	{
		if (totalBytes <= 0)
		{
			int fallback = requestedMb <= 0 ? 4096 : Math.Clamp(requestedMb, PreferredFloorMb, 16384);
			return new RamBudget
			{
				Known = false,
				SafeMaxMb = 16384,
				FloorMb = PreferredFloorMb,
				RecommendedMb = 4096,
				ChosenMb = fallback,
				Caption = "Installed memory was not detected, so this slider stays within 2 GB and 16 GB."
			};
		}
		int totalMb = (int)Math.Min(int.MaxValue, totalBytes / (1024L * 1024L));
		int reserve = Math.Max(totalMb / 4, ReserveFloorMb);
		int safe = Math.Max(0, totalMb - reserve) / StepMb * StepMb;
		int floor = safe >= PreferredFloorMb ? PreferredFloorMb : safe;
		int recommend = safe == 0 ? 0 : Math.Min(safe, Math.Max(6144, totalMb / 4));
		recommend = recommend / StepMb * StepMb;
		if (recommend < floor)
		{
			recommend = floor;
		}
		int chosen = requestedMb <= 0 ? recommend : Math.Clamp(requestedMb, floor, Math.Max(floor, safe));
		int availableMb = availableBytes <= 0 ? 0 : (int)Math.Min(int.MaxValue, availableBytes / (1024L * 1024L));
		string warning = availableMb > 0 && chosen > availableMb
			? "This choice is larger than the memory free right now (" + FormatBytes(availableBytes) + " free)."
			: "";
		string caption = safe == 0
			? "This PC does not have a safe amount of memory to dedicate. " + FormatMb(reserve) + " stays with the system."
			: "Usable up to " + FormatMb(safe) + ". " + FormatMb(reserve) + " stays with the system.";
		return new RamBudget
		{
			Known = true,
			TotalMb = totalMb,
			AvailableMb = availableMb,
			ReserveMb = reserve,
			SafeMaxMb = safe,
			FloorMb = floor,
			RecommendedMb = recommend,
			ChosenMb = chosen,
			Warning = warning,
			Caption = caption
		};
	}

	public static string FormatMb(int mb)
	{
		return FormatBytes(mb * 1024L * 1024L);
	}

	public static string FormatBytes(long bytes)
	{
		double gb = bytes / (1024d * 1024d * 1024d);
		string text = gb >= 10d ? Math.Round(gb).ToString("0", CultureInfo.InvariantCulture) : gb.ToString("0.#", CultureInfo.InvariantCulture);
		return text + " GB";
	}
}

public static class HostMemory
{
	public static Func<(long Total, long Available)>? Override { get; set; }

	public static (long Total, long Available) Read()
	{
		if (Override != null)
		{
			try
			{
				return Override();
			}
			catch (Exception ex)
			{
				LauncherLog.Write("memory probe " + ex.GetType().Name);
				return (0L, 0L);
			}
		}
		if (OperatingSystem.IsWindows())
		{
			return ReadWindows();
		}
		try
		{
			return ParseMeminfo(File.ReadAllText("/proc/meminfo"));
		}
		catch (Exception ex)
		{
			LauncherLog.Write("memory probe " + ex.GetType().Name);
			return (0L, 0L);
		}
	}

	public static (long Total, long Available) ParseMeminfo(string text)
	{
		long total = 0L;
		long available = 0L;
		string[] lines = (text ?? "").Split('\n');
		for (int i = 0; i < lines.Length; i++)
		{
			string line = lines[i].Trim();
			if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
			{
				total = Kib(line) * 1024L;
			}
			else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
			{
				available = Kib(line) * 1024L;
			}
		}
		return (total, available);
	}

	private static long Kib(string line)
	{
		string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		return parts.Length > 1 && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : 0L;
	}

	private static (long Total, long Available) ReadWindows()
	{
		try
		{
			MemoryStatusEx status = new MemoryStatusEx
			{
				dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>()
			};
			if (!GlobalMemoryStatusEx(ref status) || status.ullTotalPhys == 0)
			{
				return (0L, 0L);
			}
			return ((long)status.ullTotalPhys, (long)status.ullAvailPhys);
		}
		catch (Exception ex)
		{
			LauncherLog.Write("memory probe " + ex.GetType().Name);
			return (0L, 0L);
		}
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
	private struct MemoryStatusEx
	{
		public uint dwLength;

		public uint dwMemoryLoad;

		public ulong ullTotalPhys;

		public ulong ullAvailPhys;

		public ulong ullTotalPageFile;

		public ulong ullAvailPageFile;

		public ulong ullTotalVirtual;

		public ulong ullAvailVirtual;

		public ulong ullAvailExtendedVirtual;
	}
}

public readonly record struct CpuInfo(string Name, int Cores, int Threads, int Mhz);

public static class HostCpu
{
	public static Func<CpuInfo>? Override { get; set; }

	public static CpuInfo Read()
	{
		if (Override != null)
		{
			try
			{
				return Override();
			}
			catch (Exception ex)
			{
				LauncherLog.Write("cpu probe " + ex.GetType().Name);
				return new CpuInfo("", 0, Environment.ProcessorCount, 0);
			}
		}
		if (OperatingSystem.IsWindows())
		{
			return ReadWindows();
		}
		try
		{
			return ParseCpuinfo(File.Exists("/proc/cpuinfo") ? File.ReadAllText("/proc/cpuinfo") : "");
		}
		catch (Exception ex)
		{
			LauncherLog.Write("cpu probe " + ex.GetType().Name);
			return new CpuInfo("", 0, Environment.ProcessorCount, 0);
		}
	}

	public static CpuInfo ParseCpuinfo(string text)
	{
		string name = "";
		int mhz = 0;
		int cores = 0;
		int threads = 0;
		string[] lines = (text ?? "").Split('\n');
		for (int i = 0; i < lines.Length; i++)
		{
			string line = lines[i];
			int split = line.IndexOf(':');
			if (split <= 0)
			{
				continue;
			}
			string key = line.Substring(0, split).Trim();
			string value = line.Substring(split + 1).Trim();
			if (name.Length == 0 && key.Equals("model name", StringComparison.Ordinal))
			{
				name = value;
			}
			else if (mhz == 0 && key.Equals("cpu MHz", StringComparison.Ordinal) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double clock))
			{
				mhz = (int)Math.Round(clock);
			}
			else if (key.Equals("cpu cores", StringComparison.Ordinal) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int reported) && reported > cores)
			{
				cores = reported;
			}
			else if (key.Equals("processor", StringComparison.Ordinal))
			{
				threads++;
			}
		}
		if (threads <= 0)
		{
			threads = Environment.ProcessorCount;
		}
		if (cores <= 0)
		{
			cores = threads;
		}
		return new CpuInfo(name, cores, threads, mhz);
	}

	private static CpuInfo ReadWindows()
	{
		try
		{
			var rows = WmiQuery.Rows("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor", "Name", "NumberOfCores", "NumberOfLogicalProcessors", "MaxClockSpeed");
			if (rows == null || rows.Count == 0)
			{
				return new CpuInfo("", 0, Environment.ProcessorCount, 0);
			}
			string name = "";
			int cores = 0;
			int threads = 0;
			int mhz = 0;
			for (int i = 0; i < rows.Count; i++)
			{
				if (name.Length == 0)
				{
					name = WmiQuery.Text(rows[i], "Name");
				}
				cores += (int)WmiQuery.Number(rows[i], "NumberOfCores");
				threads += (int)WmiQuery.Number(rows[i], "NumberOfLogicalProcessors");
				int clock = (int)WmiQuery.Number(rows[i], "MaxClockSpeed");
				if (clock > mhz)
				{
					mhz = clock;
				}
			}
			if (threads <= 0)
			{
				threads = Environment.ProcessorCount;
			}
			if (cores <= 0)
			{
				cores = threads;
			}
			return new CpuInfo(name, cores, threads, mhz);
		}
		catch (Exception ex)
		{
			LauncherLog.Write("cpu probe " + ex.GetType().Name);
			return new CpuInfo("", 0, Environment.ProcessorCount, 0);
		}
	}
}
