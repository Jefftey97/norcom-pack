using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Norcom.Admin.Core;

public readonly record struct JavaSeen(string CommandLine, string WorkingDirectory);

public static class ServerPresence
{
    public const string DefaultLevel = "world_norcom";

    public static bool IsOnline(string serverDirectory, bool localProbeOk) =>
        IsOnline(serverDirectory, localProbeOk, ReadJava());

    public static bool IsOnline(string serverDirectory, bool localProbeOk, IEnumerable<JavaSeen> processes) =>
        localProbeOk || SessionLockExists(serverDirectory) || JavaServes(serverDirectory, processes);

    public static bool SessionLockExists(string serverDirectory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(serverDirectory))
            {
                return false;
            }

            return File.Exists(Path.Combine(serverDirectory, LevelName(serverDirectory), "session.lock"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static string WorldDirectory(string serverDirectory) =>
        Path.Combine(serverDirectory, LevelName(serverDirectory));

    public static bool JavaServes(string serverDirectory) =>
        JavaServes(serverDirectory, ReadJava());

    public static string ServingCommand(string serverDirectory)
    {
        if (string.IsNullOrWhiteSpace(serverDirectory))
        {
            return "";
        }

        try
        {
            foreach (var process in ReadJava())
            {
                if (CommandMatches(process.CommandLine, process.WorkingDirectory, serverDirectory))
                {
                    return process.CommandLine ?? "";
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return "";
    }

    public static bool JavaServes(string serverDirectory, IEnumerable<JavaSeen> processes)
    {
        foreach (var process in processes)
        {
            if (CommandMatches(process.CommandLine, process.WorkingDirectory, serverDirectory))
            {
                return true;
            }
        }

        return false;
    }

    public static string LevelName(string serverDirectory)
    {
        try
        {
            var path = Path.Combine(serverDirectory, "server.properties");
            if (!File.Exists(path))
            {
                return DefaultLevel;
            }

            foreach (var raw in SharedFile.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                var split = line.IndexOf('=');
                if (split <= 0 || !line[..split].Trim().Equals("level-name", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = line[(split + 1)..].Trim();
                if (value.Length == 0 || value.Contains("..", StringComparison.Ordinal) || value.IndexOfAny(['/', '\\', ':']) >= 0)
                {
                    return DefaultLevel;
                }

                return value;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return DefaultLevel;
    }

    public static bool CommandMatches(string? commandLine, string? workingDirectory, string serverDirectory)
    {
        if (string.IsNullOrWhiteSpace(commandLine) || string.IsNullOrWhiteSpace(serverDirectory))
        {
            return false;
        }

        var line = commandLine.Replace('/', '\\');
        if (!line.Contains("fabric-server-launch.jar", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains("server.jar", StringComparison.OrdinalIgnoreCase) &&
            !JarTokenUnder(line, serverDirectory))
        {
            return false;
        }

        var root = Normalize(serverDirectory);
        if (line.Contains(root, StringComparison.OrdinalIgnoreCase) || JarTokenUnder(line, serverDirectory))
        {
            return true;
        }

        var cwd = Normalize(workingDirectory);
        return cwd.Length > 0 && cwd.Equals(root, StringComparison.OrdinalIgnoreCase);
    }

    private static bool JarTokenUnder(string line, string serverDirectory)
    {
        var marker = line.IndexOf("-jar", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return false;
        }

        var rest = line[(marker + 4)..].TrimStart();
        if (rest.Length == 0)
        {
            return false;
        }

        string token;
        if (rest[0] == '"')
        {
            var end = rest.IndexOf('"', 1);
            token = end < 0 ? rest[1..] : rest[1..end];
        }
        else
        {
            var end = rest.IndexOf(' ');
            token = end < 0 ? rest : rest[..end];
        }

        if (!token.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || !Path.IsPathRooted(token))
        {
            return false;
        }

        var full = Normalize(token);
        var root = Normalize(serverDirectory);
        return full.Equals(root, StringComparison.OrdinalIgnoreCase) ||
               full.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        var trimmed = path.Trim().Replace('/', '\\').TrimEnd('\\');
        if (trimmed.Length >= 2 && trimmed[1] == ':')
        {
            return trimmed;
        }

        try
        {
            return Path.GetFullPath(path).TrimEnd('\\', '/');
        }
        catch
        {
            return trimmed;
        }
    }

    private static IReadOnlyList<JavaSeen> ReadJava()
    {
        try
        {
            return WindowsJavaScan.Read();
        }
        catch
        {
            return Array.Empty<JavaSeen>();
        }
    }
}

static class WindowsJavaScan
{
    public static IReadOnlyList<JavaSeen> Read()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<JavaSeen>();
        }

        var list = new List<JavaSeen>();
        foreach (var name in new[] { "java", "javaw" })
        {
            Process[] found;
            try
            {
                found = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            foreach (var process in found)
            {
                try
                {
                    var line = QueryCommandLine(process.Handle);
                    var cwd = QueryCwd(process.Handle);
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        list.Add(new JavaSeen(line, cwd));
                    }
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        return list;
    }

    private static string QueryCommandLine(IntPtr handle)
    {
        var length = 0;
        _ = NtQueryInformationProcess(handle, 60, IntPtr.Zero, 0, ref length);
        if (length <= 0)
        {
            return "";
        }

        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            var status = NtQueryInformationProcess(handle, 60, buffer, length, ref length);
            if (status != 0)
            {
                return "";
            }

            var size = Marshal.ReadInt16(buffer);
            if (size <= 0)
            {
                return "";
            }

            var raw = Marshal.ReadIntPtr(buffer, IntPtr.Size);
            var start = buffer.ToInt64();
            var point = raw.ToInt64();
            if (point >= start && point < start + length)
            {
                return Marshal.PtrToStringUni(raw, size / 2) ?? "";
            }

            if (point > 0 && point < length)
            {
                return Marshal.PtrToStringUni(IntPtr.Add(buffer, (int)point), size / 2) ?? "";
            }

            return "";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string QueryCwd(IntPtr handle)
    {
        if (IntPtr.Size != 8)
        {
            return "";
        }

        var basic = new byte[48];
        var length = 0;
        if (NtQueryInformationProcess(handle, 0, basic, basic.Length, ref length) != 0)
        {
            return "";
        }

        var peb = BitConverter.ToInt64(basic, 8);
        var parameters = ReadPointer(handle, peb + 0x20);
        if (parameters == 0)
        {
            return "";
        }

        var text = ReadUnicode(handle, parameters + 0x38);
        return string.IsNullOrWhiteSpace(text) ? "" : text;
    }

    private static long ReadPointer(IntPtr handle, long address)
    {
        var buffer = new byte[8];
        if (!ReadProcessMemory(handle, new IntPtr(address), buffer, buffer.Length, out var read) || read.ToInt64() < 8)
        {
            return 0;
        }

        return BitConverter.ToInt64(buffer, 0);
    }

    private static string ReadUnicode(IntPtr handle, long address)
    {
        var header = new byte[16];
        if (!ReadProcessMemory(handle, new IntPtr(address), header, header.Length, out var read) || read.ToInt64() < 16)
        {
            return "";
        }

        var size = BitConverter.ToUInt16(header, 0);
        var buffer = BitConverter.ToInt64(header, 8);
        if (size == 0 || size > 2048 || buffer == 0)
        {
            return "";
        }

        var bytes = new byte[size];
        if (!ReadProcessMemory(handle, new IntPtr(buffer), bytes, bytes.Length, out var got) || got.ToInt64() < 2)
        {
            return "";
        }

        return Encoding.Unicode.GetString(bytes, 0, (int)got.ToInt64()).TrimEnd('\0');
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass, IntPtr processInformation, int processInformationLength, ref int returnLength);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass, byte[] processInformation, int processInformationLength, ref int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int nSize, out IntPtr lpNumberOfBytesRead);
}
