using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Norcom.Launcher.Core.Services;

public static class ServerStatusClient
{
	public static byte[] BuildHandshake(string host, int port, int protocol)
	{
		using MemoryStream memoryStream = new MemoryStream();
		WriteVarInt(memoryStream, 0);
		WriteVarInt(memoryStream, protocol);
		WriteString(memoryStream, host);
		memoryStream.WriteByte((byte)(port >> 8));
		memoryStream.WriteByte((byte)port);
		WriteVarInt(memoryStream, 1);
		using MemoryStream memoryStream2 = new MemoryStream();
		WriteVarInt(memoryStream2, (int)memoryStream.Length);
		memoryStream2.Write(memoryStream.ToArray());
		return memoryStream2.ToArray();
	}

	public static async Task<ServerStatus> PingAsync(string host, int port, int protocol, CancellationToken ct)
	{
		_ = 10;
		try
		{
			TcpClient tcp = new TcpClient();
			try
			{
				using (ct.Register(delegate
				{
					tcp.Dispose();
				}))
				{
					Task connectTask = tcp.ConnectAsync(host, port);
					if (await Task.WhenAny(connectTask, Task.Delay(2500, ct)).ConfigureAwait(continueOnCapturedContext: false) != connectTask || !tcp.Connected)
					{
						return new ServerStatus(Online: false, 0, 0, "", -1, Array.Empty<string>(), "Timed out reaching the server.");
					}
					await connectTask.ConfigureAwait(continueOnCapturedContext: false);
					NetworkStream stream = tcp.GetStream();
					ServerStatus result;
					try
					{
						stream.ReadTimeout = 2500;
						stream.WriteTimeout = 2500;
						byte[] array = BuildHandshake(host, port, protocol);
						await stream.WriteAsync(array, ct).ConfigureAwait(continueOnCapturedContext: false);
						await stream.WriteAsync(new byte[2] { 1, 0 }, ct).ConfigureAwait(continueOnCapturedContext: false);
						int length = await ReadVarIntAsync(stream, ct).ConfigureAwait(continueOnCapturedContext: false);
						byte[] buf = new byte[length];
						int read;
						int num;
						for (read = 0; read < length; read += num)
						{
							num = await stream.ReadAsync(buf.AsMemory(read, length - read), ct).ConfigureAwait(continueOnCapturedContext: false);
							if (num <= 0)
							{
								break;
							}
						}
						using MemoryStream payload = new MemoryStream(buf, 0, read);
						await ReadVarIntAsync(payload, ct).ConfigureAwait(continueOnCapturedContext: false);
						int num2 = await ReadVarIntAsync(payload, ct).ConfigureAwait(continueOnCapturedContext: false);
						byte[] array2 = new byte[num2];
						int count = payload.Read(array2, 0, num2);
						string json = Encoding.UTF8.GetString(array2, 0, count);
						Stopwatch sw = Stopwatch.StartNew();
						await stream.WriteAsync(new byte[10] { 9, 1, 0, 0, 0, 0, 0, 0, 0, 0 }, ct).ConfigureAwait(continueOnCapturedContext: false);
						try
						{
							await ReadVarIntAsync(stream, ct).ConfigureAwait(continueOnCapturedContext: false);
						}
						catch
						{
						}
						sw.Stop();
						using JsonDocument jsonDocument = JsonDocument.Parse(json);
						JsonElement rootElement = jsonDocument.RootElement;
						JsonElement value;
						JsonElement jsonElement = (rootElement.TryGetProperty("players", out value) ? value : default(JsonElement));
						JsonElement value2;
						int players = ((jsonElement.ValueKind == JsonValueKind.Object && jsonElement.TryGetProperty("online", out value2)) ? value2.GetInt32() : 0);
						JsonElement value3;
						int max = ((jsonElement.ValueKind == JsonValueKind.Object && jsonElement.TryGetProperty("max", out value3)) ? value3.GetInt32() : 0);
						List<string> list = new List<string>();
						if (jsonElement.ValueKind == JsonValueKind.Object && jsonElement.TryGetProperty("sample", out var value4) && value4.ValueKind == JsonValueKind.Array)
						{
							foreach (JsonElement item in value4.EnumerateArray())
							{
								if (item.TryGetProperty("name", out var value5))
								{
									string @string = value5.GetString();
									if (@string != null)
									{
										list.Add(@string);
									}
								}
							}
						}
						string motd = "";
						if (rootElement.TryGetProperty("description", out var value6))
						{
							motd = Strip(Flatten(value6));
						}
						result = new ServerStatus(Online: true, players, max, motd, (int)sw.ElapsedMilliseconds, list, "");
					}
					finally
					{
						if (stream != null)
						{
							await stream.DisposeAsync();
						}
					}
					return result;
				}
			}
			finally
			{
				if (tcp != null)
				{
					((IDisposable)tcp).Dispose();
				}
			}
			IL_09aa:
			ServerStatus result2;
			return result2;
		}
		catch (Exception ex)
		{
			return new ServerStatus(Online: false, 0, 0, "", -1, Array.Empty<string>(), ex.Message);
		}
	}

	private static string Flatten(JsonElement el)
	{
		if (el.ValueKind == JsonValueKind.String)
		{
			return el.GetString() ?? "";
		}
		StringBuilder stringBuilder = new StringBuilder();
		if (el.ValueKind == JsonValueKind.Object)
		{
			if (el.TryGetProperty("text", out var value) && value.ValueKind == JsonValueKind.String)
			{
				stringBuilder.Append(value.GetString());
			}
			if (el.TryGetProperty("extra", out var value2) && value2.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement item in value2.EnumerateArray())
				{
					stringBuilder.Append(Flatten(item));
				}
			}
		}
		return stringBuilder.ToString();
	}

	private static string Strip(string motd)
	{
		StringBuilder stringBuilder = new StringBuilder(motd.Length);
		for (int i = 0; i < motd.Length; i++)
		{
			if (motd[i] == '§' && i + 1 < motd.Length)
			{
				i++;
			}
			else
			{
				stringBuilder.Append(motd[i]);
			}
		}
		return stringBuilder.ToString().Trim();
	}

	private static void WriteVarInt(Stream s, int v)
	{
		uint num = (uint)v;
		while ((num & 0xFFFFFF80u) != 0)
		{
			s.WriteByte((byte)((num & 0x7Fu) | 0x80u));
			num >>= 7;
		}
		s.WriteByte((byte)num);
	}

	private static void WriteString(Stream s, string text)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(text);
		WriteVarInt(s, bytes.Length);
		s.Write(bytes);
	}

	private static async Task<int> ReadVarIntAsync(Stream s, CancellationToken ct)
	{
		int num = 0;
		int shift = 0;
		do
		{
			byte[] buffer = new byte[1];
			if (await s.ReadAsync(buffer, ct).ConfigureAwait(continueOnCapturedContext: false) <= 0)
			{
				throw new EndOfStreamException();
			}
			byte b = buffer[0];
			num |= (b & 0x7F) << shift;
			if ((b & 0x80) == 0)
			{
				return num;
			}
			shift += 7;
		}
		while (shift <= 35);
		throw new InvalidDataException("varint");
	}
}
