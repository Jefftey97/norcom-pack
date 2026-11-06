using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Norcom.Launcher.Services;

internal static class DxgiVram
{
	private delegate int EnumAdapters1(nint self, uint index, out nint adapter);

	private delegate int GetDesc1(nint self, ref DxgiAdapterDesc1 desc);

	private delegate uint ReleaseCom(nint self);

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

		public long Luid;

		public uint Flags;
	}

	public static IReadOnlyList<(string Name, long Bytes)> Read()
	{
		List<(string, long)> list = new List<(string, long)>();
		Guid riid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
		if (CreateDXGIFactory1(ref riid, out var factory) != 0 || factory == IntPtr.Zero)
		{
			return list;
		}
		try
		{
			EnumAdapters1 enumAdapters = Vtable<EnumAdapters1>(factory, 12);
			nint adapter;
			for (uint num = 0u; enumAdapters(factory, num, out adapter) == 0; num++)
			{
				try
				{
					GetDesc1 getDesc = Vtable<GetDesc1>(adapter, 10);
					DxgiAdapterDesc1 desc = default(DxgiAdapterDesc1);
					if (getDesc(adapter, ref desc) == 0 && (desc.Flags & 2) == 0 && desc.DedicatedVideoMemory != 0 && !string.IsNullOrWhiteSpace(desc.Description))
					{
						list.Add((desc.Description, (long)desc.DedicatedVideoMemory));
					}
				}
				finally
				{
					Vtable<ReleaseCom>(adapter, 2)(adapter);
				}
			}
			return list;
		}
		catch
		{
			return list;
		}
		finally
		{
			Vtable<ReleaseCom>(factory, 2)(factory);
		}
	}

	private static T Vtable<T>(nint com, int slot) where T : Delegate
	{
		return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(com), slot * IntPtr.Size));
	}

	[DllImport("dxgi.dll")]
	private static extern int CreateDXGIFactory1(ref Guid riid, out nint factory);
}
