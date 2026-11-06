using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Norcom.Launcher.Core.Text;
using Norcom.Launcher.Core;

namespace Norcom.Launcher.Core.Services;

public readonly record struct FriendsEndpoint(string Host, int Port, string Kind)
{
	public string Display => Host + ":" + Port;
}

public readonly record struct SrvTarget(int Priority, int Weight, int Port, string Host);

public static class FriendsServer
{
	public const string PublicHost = NorcomPaths.ServerHost;

	public const int FallbackPort = 60313;

	public const string LoopbackHost = "127.0.0.1";

	public const int LoopbackPort = 25565;

	public const string SrvName = "_minecraft._tcp.shrimp-cub.tun.ply.gg";

	public const string HostPcVariable = "NORCOM_HOST_PC";

	public static string FallbackDisplay => PublicHost + ":" + FallbackPort;

	public static string LoopbackDisplay => LoopbackHost + ":" + LoopbackPort;

	public static FriendsEndpoint Resolve()
	{
		FriendsEndpoint endpoint = Resolve(IsHostPc(), QuerySrv);
		LauncherLog.Write("friends endpoint " + endpoint.Display + " " + endpoint.Kind);
		return endpoint;
	}

	public static FriendsEndpoint Resolve(bool hostPc, Func<IReadOnlyList<SrvTarget>>? query)
	{
		if (hostPc)
		{
			return new FriendsEndpoint(LoopbackHost, LoopbackPort, "host-loopback");
		}
		IReadOnlyList<SrvTarget> found;
		try
		{
			found = query?.Invoke() ?? Array.Empty<SrvTarget>();
		}
		catch (Exception ex)
		{
			LauncherLog.Write("srv lookup failed " + ex.GetType().Name);
			found = Array.Empty<SrvTarget>();
		}
		FriendsEndpoint? picked = Pick(found);
		if (picked.HasValue)
		{
			return picked.Value;
		}
		return new FriendsEndpoint(PublicHost, FallbackPort, "fallback");
	}

	public static bool IsHostPc()
	{
		string? flag = Environment.GetEnvironmentVariable(HostPcVariable);
		if (!string.IsNullOrWhiteSpace(flag))
		{
			string text = flag.Trim();
			if (text == "0" || text.Equals("false", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			if (text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		try
		{
			if (Directory.Exists("G:\\MinecraftServer"))
			{
				return true;
			}
		}
		catch (Exception)
		{
		}
		return false;
	}

	public static FriendsEndpoint? Pick(IReadOnlyList<SrvTarget> records)
	{
		SrvTarget? best = null;
		foreach (SrvTarget record in records)
		{
			if (record.Port < 1 || record.Port > 65535 || string.IsNullOrWhiteSpace(record.Host) || record.Host == ".")
			{
				continue;
			}
			if (best == null || record.Priority < best.Value.Priority || (record.Priority == best.Value.Priority && record.Weight > best.Value.Weight))
			{
				best = record;
			}
		}
		if (best == null)
		{
			return null;
		}
		return new FriendsEndpoint(best.Value.Host.Trim('.'), best.Value.Port, "srv");
	}

	public static IReadOnlyList<SrvTarget> ParseSrv(byte[] message)
	{
		List<SrvTarget> list = new List<SrvTarget>();
		if (message == null || message.Length < 12)
		{
			return list;
		}
		int count = ReadU16(message, 6);
		int pos = 12;
		if (!SkipName(message, ref pos) || pos + 4 > message.Length)
		{
			return list;
		}
		pos += 4;
		for (int i = 0; i < count; i++)
		{
			if (!SkipName(message, ref pos) || pos + 10 > message.Length)
			{
				break;
			}
			int type = ReadU16(message, pos);
			pos += 8;
			int length = ReadU16(message, pos);
			pos += 2;
			if (pos + length > message.Length)
			{
				break;
			}
			if (type == 33 && length >= 6)
			{
				int priority = ReadU16(message, pos);
				int weight = ReadU16(message, pos + 2);
				int port = ReadU16(message, pos + 4);
				int namePos = pos + 6;
				if (TryReadName(message, ref namePos, out string host))
				{
					list.Add(new SrvTarget(priority, weight, port, host));
				}
			}
			pos += length;
		}
		return list;
	}

	public static IReadOnlyList<SrvTarget> QuerySrv()
	{
		byte[] query = BuildQuery(SrvName);
		foreach (IPAddress dns in DnsServers())
		{
			try
			{
				using UdpClient udp = new UdpClient();
				udp.Client.ReceiveTimeout = 800;
				udp.Client.SendTimeout = 800;
				udp.Connect(dns, 53);
				udp.Send(query, query.Length);
				IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
				byte[] response = udp.Receive(ref remote);
				IReadOnlyList<SrvTarget> parsed = ParseSrv(response);
				if (parsed.Count > 0)
				{
					return parsed;
				}
			}
			catch (Exception)
			{
			}
		}
		return Array.Empty<SrvTarget>();
	}

	private static byte[] BuildQuery(string name)
	{
		using MemoryStream stream = new MemoryStream();
		WriteU16(stream, 0x4E43);
		WriteU16(stream, 0x0100);
		WriteU16(stream, 1);
		WriteU16(stream, 0);
		WriteU16(stream, 0);
		WriteU16(stream, 0);
		foreach (string label in name.Trim('.').Split('.', StringSplitOptions.RemoveEmptyEntries))
		{
			byte[] bytes = System.Text.Encoding.ASCII.GetBytes(label);
			stream.WriteByte((byte)bytes.Length);
			stream.Write(bytes);
		}
		stream.WriteByte(0);
		WriteU16(stream, 33);
		WriteU16(stream, 1);
		return stream.ToArray();
	}

	private static List<IPAddress> DnsServers()
	{
		List<IPAddress> list = new List<IPAddress>();
		try
		{
			foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
			{
				if (nic.OperationalStatus != OperationalStatus.Up)
				{
					continue;
				}
				foreach (IPAddress dns in nic.GetIPProperties().DnsAddresses)
				{
					if (dns.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(dns))
					{
						AddDns(list, dns);
					}
				}
			}
		}
		catch (Exception)
		{
		}
		try
		{
			if (File.Exists("/etc/resolv.conf"))
			{
				foreach (string line in File.ReadAllLines("/etc/resolv.conf"))
				{
					string trimmed = line.Trim();
					if (trimmed.StartsWith("nameserver", StringComparison.Ordinal) && IPAddress.TryParse(trimmed.Substring("nameserver".Length).Trim(), out IPAddress parsed) && parsed.AddressFamily == AddressFamily.InterNetwork)
					{
						AddDns(list, parsed);
					}
				}
			}
		}
		catch (Exception)
		{
		}
		AddDns(list, IPAddress.Parse("1.1.1.1"));
		AddDns(list, IPAddress.Parse("8.8.8.8"));
		if (list.Count > 3)
		{
			list.RemoveRange(3, list.Count - 3);
		}
		return list;
	}

	private static void AddDns(List<IPAddress> list, IPAddress dns)
	{
		if (!list.Contains(dns))
		{
			list.Add(dns);
		}
	}

	private static bool SkipName(byte[] message, ref int pos)
	{
		return TryReadName(message, ref pos, out _);
	}

	private static bool TryReadName(byte[] message, ref int pos, out string host)
	{
		host = "";
		List<string> labels = new List<string>();
		int guard = 0;
		int jump = -1;
		int cursor = pos;
		while (cursor < message.Length && guard++ < 32)
		{
			int length = message[cursor];
			if (length == 0)
			{
				cursor++;
				if (jump < 0)
				{
					pos = cursor;
				}
				else
				{
					pos = jump;
				}
				host = string.Join('.', labels);
				return true;
			}
			if ((length & 0xC0) == 0xC0)
			{
				if (cursor + 1 >= message.Length)
				{
					return false;
				}
				if (jump < 0)
				{
					jump = cursor + 2;
				}
				cursor = ((length & 0x3F) << 8) | message[cursor + 1];
				continue;
			}
			cursor++;
			if (cursor + length > message.Length)
			{
				return false;
			}
			labels.Add(System.Text.Encoding.ASCII.GetString(message, cursor, length));
			cursor += length;
		}
		return false;
	}

	private static int ReadU16(byte[] message, int pos)
	{
		return (message[pos] << 8) | message[pos + 1];
	}

	private static void WriteU16(Stream stream, int value)
	{
		stream.WriteByte((byte)(value >> 8));
		stream.WriteByte((byte)value);
	}
}
