using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Norcom.Launcher.Core.Services;

public sealed class MachineSpecs
{
	public string GpuName { get; init; } = "";

	public long VramBytes { get; init; }

	public string CpuName { get; init; } = "";

	public int PhysicalCores { get; init; }

	public int LogicalCores { get; init; }

	public int CpuMhz { get; init; }

	public long RamBytes { get; init; }

	public long FreeRamBytes { get; init; }

	public int DisplayWidth { get; init; }

	public int DisplayHeight { get; init; }

	public int RefreshHz { get; init; }

	public IReadOnlyList<string> Gaps { get; init; } = Array.Empty<string>();

	public bool HasGraphicsMemory => VramBytes > 0 && RamBytes > 0;
}

public static class MachineSpecsProbe
{
	public static Func<MachineSpecs>? Override { get; set; }

	public static MachineSpecs Read()
	{
		if (Override != null)
		{
			try
			{
				return Override();
			}
			catch (Exception ex)
			{
				return new MachineSpecs
				{
					LogicalCores = Environment.ProcessorCount,
					Gaps = new string[1] { "Spec detection failed (" + ex.GetType().Name + "). Low is the safe preset." }
				};
			}
		}
		List<string> gaps = new List<string>();
		int cores = Environment.ProcessorCount;
		int mhz = 0;
		long ram = 0L;
		string gpu = "";
		long vram = 0L;
		int width = 0;
		int height = 0;
		int refresh = 0;
		(long ramBytes, long freeBytes) = HostMemory.Read();
		CpuInfo cpu = HostCpu.Read();
		if (cpu.Threads > 0)
		{
			cores = cpu.Threads;
		}
		mhz = cpu.Mhz;
		ram = ramBytes;
		if (!OperatingSystem.IsWindows())
		{
			gaps.Add("GPU and video memory are read with DXGI and WMI on Windows.");
			return new MachineSpecs
			{
				CpuName = cpu.Name,
				PhysicalCores = cpu.Cores,
				LogicalCores = cores,
				CpuMhz = mhz,
				RamBytes = ram,
				FreeRamBytes = freeBytes,
				Gaps = gaps
			};
		}
		try
		{
			if (DxgiDisplay.TryPrimary(out string dxgiName, out long dxgiBytes) && dxgiBytes > 0)
			{
				gpu = dxgiName;
				vram = dxgiBytes;
			}
		}
		catch (Exception ex)
		{
			gaps.Add("DXGI was not readable (" + ex.GetType().Name + ").");
		}
		if (cpu.Name.Length == 0)
		{
			gaps.Add("CPU details were not readable.");
		}
		if (ram <= 0)
		{
			gaps.Add("Installed memory was not readable.");
		}
		try
		{
			List<Dictionary<string, object?>>? adapters = WmiQuery.Rows("SELECT Name, AdapterRAM, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate FROM Win32_VideoController", "Name", "AdapterRAM", "CurrentHorizontalResolution", "CurrentVerticalResolution", "CurrentRefreshRate");
			if (adapters == null)
			{
				gaps.Add("Display adapter was not readable.");
			}
			else
			{
				foreach (Dictionary<string, object?> row in adapters)
				{
					string name = WmiQuery.Text(row, "Name");
					long bytes = WmiQuery.Number(row, "AdapterRAM");
					int rowWidth = (int)WmiQuery.Number(row, "CurrentHorizontalResolution");
					int rowHeight = (int)WmiQuery.Number(row, "CurrentVerticalResolution");
					int rowRefresh = (int)WmiQuery.Number(row, "CurrentRefreshRate");
					if (rowWidth > width)
					{
						width = rowWidth;
						height = rowHeight;
						refresh = rowRefresh;
					}
					if (vram > 0)
					{
						continue;
					}
					if (name.Length == 0 || GraphicsAuto.IsIntegratedName(name))
					{
						if (gpu.Length == 0)
						{
							gpu = name;
						}
						continue;
					}
					if (bytes >= vram)
					{
						gpu = name;
						vram = bytes;
					}
				}
				if (vram <= 0)
				{
					gaps.Add("Video memory was not readable.");
				}
			}
		}
		catch (Exception ex)
		{
			gaps.Add("Display adapter was not readable (" + ex.GetType().Name + ").");
		}
		return new MachineSpecs
		{
			GpuName = gpu,
			VramBytes = vram,
			CpuName = cpu.Name,
			PhysicalCores = cpu.Cores,
			LogicalCores = cores,
			CpuMhz = mhz,
			RamBytes = ram,
			FreeRamBytes = freeBytes,
			DisplayWidth = width,
			DisplayHeight = height,
			RefreshHz = refresh,
			Gaps = gaps
		};
	}
}

internal static class DxgiDisplay
{
	public static bool TryPrimary(out string name, out long dedicatedBytes)
	{
		name = "";
		dedicatedBytes = 0L;
		if (!OperatingSystem.IsWindows())
		{
			return false;
		}
		Guid iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
		int hr = CreateDXGIFactory1(ref iid, out IntPtr factory);
		if (hr < 0 || factory == IntPtr.Zero)
		{
			return false;
		}
		try
		{
			IntPtr vtable = Marshal.ReadIntPtr(factory);
			EnumAdapters1Delegate enumerate = Marshal.GetDelegateForFunctionPointer<EnumAdapters1Delegate>(Marshal.ReadIntPtr(vtable, IntPtr.Size * 12));
			hr = enumerate(factory, 0u, out IntPtr adapter);
			if (hr < 0 || adapter == IntPtr.Zero)
			{
				return false;
			}
			try
			{
				IntPtr adapterTable = Marshal.ReadIntPtr(adapter);
				GetDesc1Delegate describe = Marshal.GetDelegateForFunctionPointer<GetDesc1Delegate>(Marshal.ReadIntPtr(adapterTable, IntPtr.Size * 10));
				DxgiAdapterDesc1 desc = default(DxgiAdapterDesc1);
				hr = describe(adapter, ref desc);
				if (hr < 0)
				{
					return false;
				}
				name = (desc.Description ?? "").Trim();
				dedicatedBytes = (long)desc.DedicatedVideoMemory;
				return name.Length > 0 && dedicatedBytes > 0;
			}
			finally
			{
				Release(adapter);
			}
		}
		finally
		{
			Release(factory);
		}
	}

	private static void Release(IntPtr com)
	{
		if (com == IntPtr.Zero)
		{
			return;
		}
		IntPtr vtable = Marshal.ReadIntPtr(com);
		ReleaseDelegate release = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(Marshal.ReadIntPtr(vtable, IntPtr.Size * 2));
		release(com);
	}

	[DllImport("dxgi.dll", ExactSpelling = true)]
	private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

	private delegate int EnumAdapters1Delegate(IntPtr self, uint index, out IntPtr adapter);

	private delegate int GetDesc1Delegate(IntPtr self, ref DxgiAdapterDesc1 desc);

	private delegate uint ReleaseDelegate(IntPtr self);

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct DxgiAdapterDesc1
	{
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string Description;

		public uint VendorId;

		public uint DeviceId;

		public uint SubSysId;

		public uint Revision;

		public nuint DedicatedVideoMemory;

		public nuint DedicatedSystemMemory;

		public nuint SharedSystemMemory;

		public long AdapterLuid;

		public uint Flags;
	}
}
