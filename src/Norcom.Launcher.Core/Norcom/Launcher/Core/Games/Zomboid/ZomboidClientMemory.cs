using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Norcom.Launcher.Core.Text;

namespace Norcom.Launcher.Core.Games.Zomboid;

/// <summary>
/// Raises the single -Xmx entry in the Project Zomboid install's ProjectZomboid64.json
/// before Play starts the game. 8g when the PC has more than 16 GB of RAM, otherwise 6g.
/// An existing value that is already at least that large is left alone, and every other
/// byte of the file stays as it was. A failure is logged and Play continues.
/// </summary>
public static class ZomboidClientMemory
{
	public const string FileName = "ProjectZomboid64.json";

	public const string BackupName = "ProjectZomboid64.json.norcom-bak";

	public const string ArgName = "--norcom-zomboid-xmx";

	public const int ExitOk = 0;

	public const int ExitFailed = 1;

	public const int ExitDeclined = 1223;

	public const long SixteenGibibytes = 16L * 1024L * 1024L * 1024L;

	public enum EditKind
	{
		Malformed,
		MissingEntry,
		AlreadySufficient,
		Replaced
	}

	public readonly struct EditResult
	{
		public EditKind Kind { get; init; }

		public string Text { get; init; }
	}

	public static string HeapForTotalBytes(long totalPhysicalBytes)
	{
		return totalPhysicalBytes > SixteenGibibytes ? "8g" : "6g";
	}

	public static bool IsElevatedRequest(string[] args)
	{
		return args != null && args.Length > 0 && string.Equals(args[0], ArgName, StringComparison.Ordinal);
	}

	public static EditResult Edit(string json, string targetHeap)
	{
		if (json == null)
		{
			return new EditResult
			{
				Kind = EditKind.Malformed,
				Text = ""
			};
		}
		if (!TryParseHeapBytes(targetHeap, out long targetBytes) || (targetHeap != "6g" && targetHeap != "8g"))
		{
			return new EditResult
			{
				Kind = EditKind.Malformed,
				Text = json
			};
		}
		try
		{
			using JsonDocument document = JsonDocument.Parse(json);
			_ = document;
		}
		catch (JsonException)
		{
			return new EditResult
			{
				Kind = EditKind.Malformed,
				Text = json
			};
		}
		if (!TryCollect(json, out List<Hit> hits))
		{
			return new EditResult
			{
				Kind = EditKind.Malformed,
				Text = json
			};
		}
		if (hits.Count == 0)
		{
			return new EditResult
			{
				Kind = EditKind.MissingEntry,
				Text = json
			};
		}
		for (int i = 0; i < hits.Count; i++)
		{
			if (!hits[i].Parsed)
			{
				return new EditResult
				{
					Kind = EditKind.Malformed,
					Text = json
				};
			}
		}
		string edited = json;
		bool replaced = false;
		for (int i = hits.Count - 1; i >= 0; i--)
		{
			Hit hit = hits[i];
			if (hit.Bytes >= targetBytes)
			{
				continue;
			}
			edited = edited.Substring(0, hit.Start) + "-Xmx" + targetHeap + edited.Substring(hit.Start + hit.Length);
			replaced = true;
		}
		if (!replaced)
		{
			return new EditResult
			{
				Kind = EditKind.AlreadySufficient,
				Text = json
			};
		}
		try
		{
			using JsonDocument document = JsonDocument.Parse(edited);
			_ = document;
		}
		catch (JsonException)
		{
			return new EditResult
			{
				Kind = EditKind.Malformed,
				Text = json
			};
		}
		return new EditResult
		{
			Kind = EditKind.Replaced,
			Text = edited
		};
	}

	public static void ApplyBeforeLaunch(string gameDirectory, long totalPhysicalBytes)
	{
		ApplyBeforeLaunch(gameDirectory, totalPhysicalBytes, null, null);
	}

	public static void ApplyBeforeLaunch(string gameDirectory, long totalPhysicalBytes, Func<string, string, int> elevate, Action<string, byte[], byte[]> commit = null)
	{
		try
		{
			string heap = HeapForTotalBytes(totalPhysicalBytes);
			string path = JsonPath(gameDirectory);
			Status status = TryApply(path, heap, commit, out bool changed);
			if (status == Status.AccessDenied)
			{
				int code;
				try
				{
					Func<string, string, int> run = elevate ?? ElevateProcess;
					code = run(path, heap);
				}
				catch (Exception ex)
				{
					LauncherLog.Write("zomboid memory elevation " + ex.GetType().Name);
					return;
				}
				if (code == ExitDeclined)
				{
					LauncherLog.Write("zomboid memory elevation declined");
				}
				else if (code != ExitOk)
				{
					LauncherLog.Write("zomboid memory elevation failed");
				}
				return;
			}
			LogStatus(status, heap, changed);
		}
		catch (Exception ex)
		{
			LauncherLog.Write("zomboid memory " + ex.GetType().Name);
		}
	}

	public static int RunElevated(string[] args)
	{
		try
		{
			if (args == null || args.Length != 3 || !string.Equals(args[0], ArgName, StringComparison.Ordinal))
			{
				return ExitFailed;
			}
			string heap = args[2];
			if (heap != "6g" && heap != "8g")
			{
				return ExitFailed;
			}
			string full;
			try
			{
				full = Path.GetFullPath(args[1]);
			}
			catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
			{
				return ExitFailed;
			}
			if (!string.Equals(Path.GetFileName(full), FileName, StringComparison.OrdinalIgnoreCase))
			{
				return ExitFailed;
			}
			string parent = Path.GetFileName(Path.GetDirectoryName(full) ?? "");
			if (!string.Equals(parent, "ProjectZomboid", StringComparison.OrdinalIgnoreCase))
			{
				return ExitFailed;
			}
			Status status = TryApply(full, heap, null, out bool changed);
			LogStatus(status, heap, changed);
			return status == Status.Done ? ExitOk : ExitFailed;
		}
		catch (Exception ex)
		{
			LauncherLog.Write("zomboid memory " + ex.GetType().Name);
			return ExitFailed;
		}
	}

	private static void LogStatus(Status status, string heap, bool changed)
	{
		if (status == Status.Done)
		{
			if (changed)
			{
				LauncherLog.Write("zomboid memory set " + heap);
			}
			return;
		}
		if (status == Status.Missing)
		{
			LauncherLog.Write("zomboid memory json missing");
		}
		else if (status == Status.Malformed)
		{
			LauncherLog.Write("zomboid memory json malformed");
		}
		else if (status == Status.Locked)
		{
			LauncherLog.Write("zomboid memory file locked");
		}
		else if (status == Status.AccessDenied)
		{
			LauncherLog.Write("zomboid memory access denied");
		}
		else
		{
			LauncherLog.Write("zomboid memory failed");
		}
	}

	private static Status TryApply(string path, string heap, Action<string, byte[], byte[]> commit, out bool changed)
	{
		changed = false;
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
		{
			return Status.Missing;
		}
		byte[] original;
		try
		{
			original = File.ReadAllBytes(path);
		}
		catch (Exception ex) when (IsLocked(ex))
		{
			return Status.Locked;
		}
		catch (Exception ex) when (IsAccessDenied(ex))
		{
			return Status.AccessDenied;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			LauncherLog.Write("zomboid memory json unreadable " + ex.GetType().Name);
			return Status.Failed;
		}
		if (!TryDecode(original, out string text, out bool bom))
		{
			return Status.Malformed;
		}
		EditResult edit = Edit(text, heap);
		if (edit.Kind == EditKind.Malformed)
		{
			return Status.Malformed;
		}
		if (edit.Kind != EditKind.Replaced)
		{
			return Status.Done;
		}
		byte[] edited = Encode(edit.Text, bom);
		try
		{
			if (commit != null)
			{
				commit(path, original, edited);
			}
			else
			{
				Commit(path, original, edited);
			}
			changed = true;
			return Status.Done;
		}
		catch (Exception ex) when (IsLocked(ex))
		{
			return Status.Locked;
		}
		catch (Exception ex) when (IsAccessDenied(ex))
		{
			return Status.AccessDenied;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			LauncherLog.Write("zomboid memory write " + ex.GetType().Name);
			return Status.Failed;
		}
	}

	private static void Commit(string path, byte[] original, byte[] edited)
	{
		string directory = Path.GetDirectoryName(path) ?? "";
		string temp = Path.Combine(directory, FileName + ".norcom-new");
		string backup = Path.Combine(directory, BackupName);
		File.WriteAllBytes(temp, edited);
		try
		{
			ClearReadOnly(path);
			if (!File.Exists(backup))
			{
				File.WriteAllBytes(backup, original);
			}
			File.Move(temp, path, overwrite: true);
		}
		catch
		{
			TryDelete(temp);
			throw;
		}
	}

	private static int ElevateProcess(string jsonPath, string heap)
	{
		string full;
		try
		{
			full = Path.GetFullPath(jsonPath);
		}
		catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
		{
			LauncherLog.Write("zomboid memory elevation unavailable");
			return ExitFailed;
		}
		if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(Environment.ProcessPath) || full.IndexOf('"') >= 0)
		{
			LauncherLog.Write("zomboid memory elevation unavailable");
			return ExitFailed;
		}
		ProcessStartInfo info = new ProcessStartInfo
		{
			FileName = Environment.ProcessPath,
			UseShellExecute = true,
			Verb = "runas",
			Arguments = ArgName + " \"" + full + "\" " + heap,
			WindowStyle = ProcessWindowStyle.Hidden
		};
		try
		{
			using Process process = Process.Start(info);
			if (process == null)
			{
				return ExitFailed;
			}
			process.WaitForExit();
			return process.ExitCode;
		}
		catch (Win32Exception ex) when (ex.NativeErrorCode == ExitDeclined)
		{
			return ExitDeclined;
		}
	}

	private static string JsonPath(string gameDirectory)
	{
		if (string.IsNullOrWhiteSpace(gameDirectory))
		{
			return "";
		}
		return Path.Combine(gameDirectory, FileName);
	}

	private static bool TryDecode(byte[] original, out string text, out bool bom)
	{
		text = "";
		bom = original.Length >= 3 && original[0] == 0xEF && original[1] == 0xBB && original[2] == 0xBF;
		int offset = bom ? 3 : 0;
		try
		{
			text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(original, offset, original.Length - offset);
			return true;
		}
		catch (Exception ex) when (ex is ArgumentException or DecoderFallbackException)
		{
			return false;
		}
	}

	private static byte[] Encode(string text, bool bom)
	{
		byte[] body = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
		if (!bom)
		{
			return body;
		}
		byte[] withBom = new byte[body.Length + 3];
		withBom[0] = 0xEF;
		withBom[1] = 0xBB;
		withBom[2] = 0xBF;
		Buffer.BlockCopy(body, 0, withBom, 3, body.Length);
		return withBom;
	}

	private static void ClearReadOnly(string path)
	{
		FileAttributes attributes = File.GetAttributes(path);
		if ((attributes & FileAttributes.ReadOnly) != 0)
		{
			File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
		}
	}

	private static void TryDelete(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
		}
	}

	private static bool IsAccessDenied(Exception ex)
	{
		if (ex is UnauthorizedAccessException)
		{
			return true;
		}
		return (ex.HResult & 0xFFFF) == 5;
	}

	private static bool IsLocked(Exception ex)
	{
		int code = ex.HResult & 0xFFFF;
		return code == 32 || code == 33;
	}

	private static bool TryParseHeapBytes(string text, out long bytes)
	{
		bytes = 0;
		if (string.IsNullOrEmpty(text))
		{
			return false;
		}
		string body = text;
		if (body.StartsWith("-Xmx", StringComparison.Ordinal))
		{
			body = body.Substring(4);
		}
		if (body.Length == 0)
		{
			return false;
		}
		long multiplier = 1;
		char suffix = body[body.Length - 1];
		if (suffix == 'k' || suffix == 'K')
		{
			multiplier = 1024L;
			body = body.Substring(0, body.Length - 1);
		}
		else if (suffix == 'm' || suffix == 'M')
		{
			multiplier = 1024L * 1024L;
			body = body.Substring(0, body.Length - 1);
		}
		else if (suffix == 'g' || suffix == 'G')
		{
			multiplier = 1024L * 1024L * 1024L;
			body = body.Substring(0, body.Length - 1);
		}
		else if (suffix < '0' || suffix > '9')
		{
			return false;
		}
		if (body.Length == 0 || body[0] == '+' || body[0] == '-')
		{
			return false;
		}
		if (!long.TryParse(body, NumberStyles.None, CultureInfo.InvariantCulture, out long value) || value < 0)
		{
			return false;
		}
		try
		{
			bytes = checked(value * multiplier);
		}
		catch (OverflowException)
		{
			return false;
		}
		return true;
	}

	private static bool TryCollect(string json, out List<Hit> hits)
	{
		Scanner scanner = new Scanner(json);
		bool ok = scanner.Parse();
		hits = scanner.Hits;
		return ok;
	}

	private struct Hit
	{
		public int Start;

		public int Length;

		public long Bytes;

		public bool Parsed;
	}

	private sealed class Scanner
	{
		private readonly string _text;

		private int _i;

		public Scanner(string text)
		{
			_text = text ?? "";
		}

		public List<Hit> Hits { get; } = new List<Hit>();

		public bool Parse()
		{
			if (!ParseValue())
			{
				return false;
			}
			SkipWs();
			return _i == _text.Length;
		}

		private bool ParseValue()
		{
			SkipWs();
			if (_i >= _text.Length)
			{
				return false;
			}
			char c = _text[_i];
			if (c == '{')
			{
				return ParseObject();
			}
			if (c == '[')
			{
				return ParseArray(false);
			}
			if (c == '"')
			{
				return ReadString(out _, out _, out _);
			}
			if (c == 't')
			{
				return Consume("true");
			}
			if (c == 'f')
			{
				return Consume("false");
			}
			if (c == 'n')
			{
				return Consume("null");
			}
			if (c == '-' || (c >= '0' && c <= '9'))
			{
				return ParseNumber();
			}
			return false;
		}

		private bool ParseObject()
		{
			_i++;
			SkipWs();
			if (ConsumeChar('}'))
			{
				return true;
			}
			while (true)
			{
				SkipWs();
				if (_i >= _text.Length || _text[_i] != '"')
				{
					return false;
				}
				if (!ReadString(out string name, out _, out _))
				{
					return false;
				}
				SkipWs();
				if (!ConsumeChar(':'))
				{
					return false;
				}
				if (string.Equals(name, "vmArgs", StringComparison.Ordinal))
				{
					SkipWs();
					if (_i < _text.Length && _text[_i] == '[')
					{
						if (!ParseArray(true))
						{
							return false;
						}
					}
					else if (!ParseValue())
					{
						return false;
					}
				}
				else if (!ParseValue())
				{
					return false;
				}
				SkipWs();
				if (ConsumeChar('}'))
				{
					return true;
				}
				if (!ConsumeChar(','))
				{
					return false;
				}
			}
		}

		private bool ParseArray(bool vmArgs)
		{
			_i++;
			SkipWs();
			if (ConsumeChar(']'))
			{
				return true;
			}
			while (true)
			{
				SkipWs();
				if (_i >= _text.Length)
				{
					return false;
				}
				if (vmArgs && _text[_i] == '"')
				{
					if (!ReadString(out string decoded, out int start, out int length))
					{
						return false;
					}
					if (decoded.StartsWith("-Xmx", StringComparison.Ordinal))
					{
						bool parsed = TryParseHeapBytes(decoded, out long bytes);
						Hits.Add(new Hit
						{
							Start = start,
							Length = length,
							Bytes = bytes,
							Parsed = parsed
						});
					}
				}
				else if (!ParseValue())
				{
					return false;
				}
				SkipWs();
				if (ConsumeChar(']'))
				{
					return true;
				}
				if (!ConsumeChar(','))
				{
					return false;
				}
			}
		}

		private bool ReadString(out string decoded, out int innerStart, out int innerLength)
		{
			decoded = "";
			innerStart = 0;
			innerLength = 0;
			if (_i >= _text.Length || _text[_i] != '"')
			{
				return false;
			}
			_i++;
			innerStart = _i;
			StringBuilder builder = new StringBuilder();
			while (_i < _text.Length)
			{
				char c = _text[_i];
				if (c == '"')
				{
					innerLength = _i - innerStart;
					_i++;
					decoded = builder.ToString();
					return true;
				}
				if (c == '\\')
				{
					_i++;
					if (_i >= _text.Length)
					{
						return false;
					}
					char escaped = _text[_i];
					_i++;
					if (escaped == '"' || escaped == '\\' || escaped == '/')
					{
						builder.Append(escaped);
						continue;
					}
					if (escaped == 'b')
					{
						builder.Append('\b');
						continue;
					}
					if (escaped == 'f')
					{
						builder.Append('\f');
						continue;
					}
					if (escaped == 'n')
					{
						builder.Append('\n');
						continue;
					}
					if (escaped == 'r')
					{
						builder.Append('\r');
						continue;
					}
					if (escaped == 't')
					{
						builder.Append('\t');
						continue;
					}
					if (escaped == 'u')
					{
						if (_i + 4 > _text.Length)
						{
							return false;
						}
						string hex = _text.Substring(_i, 4);
						_i += 4;
						if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
						{
							return false;
						}
						builder.Append((char)code);
						continue;
					}
					return false;
				}
				if (c < 0x20)
				{
					return false;
				}
				builder.Append(c);
				_i++;
			}
			return false;
		}

		private bool ParseNumber()
		{
			int start = _i;
			if (_text[_i] == '-')
			{
				_i++;
			}
			if (_i >= _text.Length || _text[_i] < '0' || _text[_i] > '9')
			{
				return false;
			}
			if (_text[_i] == '0')
			{
				_i++;
			}
			else
			{
				while (_i < _text.Length && _text[_i] >= '0' && _text[_i] <= '9')
				{
					_i++;
				}
			}
			if (_i < _text.Length && _text[_i] == '.')
			{
				_i++;
				if (_i >= _text.Length || _text[_i] < '0' || _text[_i] > '9')
				{
					return false;
				}
				while (_i < _text.Length && _text[_i] >= '0' && _text[_i] <= '9')
				{
					_i++;
				}
			}
			if (_i < _text.Length && (_text[_i] == 'e' || _text[_i] == 'E'))
			{
				_i++;
				if (_i < _text.Length && (_text[_i] == '+' || _text[_i] == '-'))
				{
					_i++;
				}
				if (_i >= _text.Length || _text[_i] < '0' || _text[_i] > '9')
				{
					return false;
				}
				while (_i < _text.Length && _text[_i] >= '0' && _text[_i] <= '9')
				{
					_i++;
				}
			}
			return _i > start;
		}

		private bool Consume(string literal)
		{
			if (_i + literal.Length > _text.Length)
			{
				return false;
			}
			for (int n = 0; n < literal.Length; n++)
			{
				if (_text[_i + n] != literal[n])
				{
					return false;
				}
			}
			_i += literal.Length;
			return true;
		}

		private bool ConsumeChar(char c)
		{
			if (_i < _text.Length && _text[_i] == c)
			{
				_i++;
				return true;
			}
			return false;
		}

		private void SkipWs()
		{
			while (_i < _text.Length)
			{
				char c = _text[_i];
				if (c != ' ' && c != '\t' && c != '\r' && c != '\n')
				{
					return;
				}
				_i++;
			}
		}
	}

	private enum Status
	{
		Done,
		Missing,
		Malformed,
		Locked,
		AccessDenied,
		Failed
	}
}
