using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Norcom.Launcher.Core.Services;

public static class ProcessWorkingDirectory
{
	public static string TryRead(int pid)
	{
		if (!OperatingSystem.IsWindows() || pid <= 0 || IntPtr.Size != 8)
		{
			return "";
		}
		IntPtr handle = IntPtr.Zero;
		try
		{
			handle = OpenProcess(0x1000 | 0x0010, false, pid);
			if (handle == IntPtr.Zero)
			{
				return "";
			}
			ProcessBasicInformation info = default;
			int status = NtQueryInformationProcess(handle, 0, ref info, Marshal.SizeOf<ProcessBasicInformation>(), out _);
			if (status != 0 || info.PebBaseAddress == IntPtr.Zero)
			{
				return "";
			}
			IntPtr parameters = ReadPointer(handle, info.PebBaseAddress + 0x20);
			if (parameters == IntPtr.Zero)
			{
				return "";
			}
			byte[] header = ReadBytes(handle, parameters + 0x38, 16);
			if (header.Length < 16)
			{
				return "";
			}
			int length = BitConverter.ToUInt16(header, 0);
			IntPtr buffer = new IntPtr(BitConverter.ToInt64(header, 8));
			if (length <= 0 || length > 1024 || buffer == IntPtr.Zero)
			{
				return "";
			}
			byte[] chars = ReadBytes(handle, buffer, length);
			return Encoding.Unicode.GetString(chars).TrimEnd('\0').Trim();
		}
		catch (Exception)
		{
			return "";
		}
		finally
		{
			if (handle != IntPtr.Zero)
			{
				CloseHandle(handle);
			}
		}
	}

	private static IntPtr ReadPointer(IntPtr process, IntPtr address)
	{
		byte[] bytes = ReadBytes(process, address, 8);
		return bytes.Length < 8 ? IntPtr.Zero : new IntPtr(BitConverter.ToInt64(bytes, 0));
	}

	private static byte[] ReadBytes(IntPtr process, IntPtr address, int size)
	{
		byte[] buffer = new byte[size];
		return ReadProcessMemory(process, address, buffer, size, out int read) && read == size ? buffer : Array.Empty<byte>();
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct ProcessBasicInformation
	{
		public IntPtr Reserved1;
		public IntPtr PebBaseAddress;
		public IntPtr Reserved2;
		public IntPtr Reserved3;
		public IntPtr UniqueProcessId;
		public IntPtr Reserved4;
	}

	[DllImport("ntdll.dll")]
	private static extern int NtQueryInformationProcess(IntPtr process, int klass, ref ProcessBasicInformation information, int length, out int returned);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out int read);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool CloseHandle(IntPtr handle);
}
