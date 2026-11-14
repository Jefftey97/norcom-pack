using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Norcom.Launcher.Core.Services;

/// <summary>What the friends' Project Zomboid server says about itself.</summary>
public sealed record ZomboidStatus(bool Online, int Players, int MaxPlayers, string Detail);

/// <summary>
/// Asks the Project Zomboid server for its status with the Steam server query (A2S_INFO), the same question
/// Steam's server browser asks. The address is the connect address from the Zomboid feed, so it follows the
/// server wherever the feed points. Zomboid only; Minecraft has its own status client.
/// </summary>
public static class ZomboidStatusClient
{
	private static readonly byte[] InfoRequest = BuildRequest(null);

	public static async Task<ZomboidStatus> ReadAsync(HttpClient http, CancellationToken ct)
	{
		ZomboidFeedDoc feed = await LoadFeedAsync(http, ct).ConfigureAwait(false);
		if (string.IsNullOrWhiteSpace(feed.Host) || feed.Port < 1 || feed.Port > 65535)
		{
			return new ZomboidStatus(false, 0, 0, "The Zomboid server address isn't published yet.");
		}
		return await QueryAsync(feed.Host, feed.Port, TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
	}

	public static async Task<ZomboidStatus> QueryAsync(string host, int port, TimeSpan timeout, CancellationToken ct)
	{
		try
		{
			IPAddress[] addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
			IPAddress? address = Array.Find(addresses, a => a.AddressFamily == AddressFamily.InterNetwork) ?? (addresses.Length > 0 ? addresses[0] : null);
			if (address == null)
			{
				return new ZomboidStatus(false, 0, 0, "The Zomboid server address could not be found.");
			}
			using UdpClient udp = new UdpClient(address.AddressFamily);
			IPEndPoint target = new IPEndPoint(address, port);
			byte[] request = InfoRequest;
			for (int attempt = 0; attempt < 3; attempt++)
			{
				await udp.SendAsync(request, request.Length, target).ConfigureAwait(false);
				using CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
				wait.CancelAfter(timeout);
				UdpReceiveResult reply;
				try
				{
					reply = await udp.ReceiveAsync(wait.Token).ConfigureAwait(false);
				}
				catch (OperationCanceledException) when (!ct.IsCancellationRequested)
				{
					continue;
				}
				byte[] data = reply.Buffer;
				if (data.Length >= 9 && data[4] == 0x41)
				{
					// Challenge: repeat the request with the 4 bytes the server sent.
					request = BuildRequest(new[] { data[5], data[6], data[7], data[8] });
					continue;
				}
				ZomboidStatus? parsed = ParseInfo(data);
				if (parsed != null)
				{
					return parsed;
				}
			}
			return new ZomboidStatus(false, 0, 0, "The Zomboid server did not answer.");
		}
		catch (Exception ex) when (ex is SocketException or IOException or ArgumentException)
		{
			return new ZomboidStatus(false, 0, 0, "The Zomboid server did not answer.");
		}
	}

	/// <summary>Parses an A2S_INFO reply (header FF FF FF FF 49). Returns null for anything else.</summary>
	public static ZomboidStatus? ParseInfo(byte[] data)
	{
		if (data == null || data.Length < 6 || data[0] != 0xFF || data[1] != 0xFF || data[2] != 0xFF || data[3] != 0xFF || data[4] != 0x49)
		{
			return null;
		}
		int i = 6; // header, type, protocol
		if (!SkipString(data, ref i) || !SkipString(data, ref i) || !SkipString(data, ref i) || !SkipString(data, ref i))
		{
			return null; // name, map, folder, game
		}
		i += 2; // app id (short)
		if (i + 2 > data.Length)
		{
			return null;
		}
		int players = data[i];
		int max = data[i + 1];
		return new ZomboidStatus(true, players, max, "Server is up.");
	}

	private static bool SkipString(byte[] data, ref int i)
	{
		int end = Array.IndexOf(data, (byte)0, i);
		if (end < 0)
		{
			return false;
		}
		i = end + 1;
		return true;
	}

	private static byte[] BuildRequest(byte[]? challenge)
	{
		byte[] head = { 0xFF, 0xFF, 0xFF, 0xFF, 0x54 };
		byte[] text = Encoding.ASCII.GetBytes("Source Engine Query\0");
		byte[] tail = challenge ?? Array.Empty<byte>();
		byte[] all = new byte[head.Length + text.Length + tail.Length];
		Buffer.BlockCopy(head, 0, all, 0, head.Length);
		Buffer.BlockCopy(text, 0, all, head.Length, text.Length);
		Buffer.BlockCopy(tail, 0, all, head.Length + text.Length, tail.Length);
		return all;
	}

	private static async Task<ZomboidFeedDoc> LoadFeedAsync(HttpClient http, CancellationToken ct)
	{
		string cache = ZomboidFeed.CachePath(NorcomPaths.NorcomHome);
		try
		{
			using CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
			wait.CancelAfter(TimeSpan.FromSeconds(6));
			string live = await http.GetStringAsync(ZomboidFeed.Url, wait.Token).ConfigureAwait(false);
			ZomboidFeedDoc doc = ZomboidFeed.Parse(live);
			if (!string.IsNullOrWhiteSpace(doc.Host))
			{
				return doc;
			}
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
		{
		}
		try
		{
			return File.Exists(cache) ? ZomboidFeed.Parse(File.ReadAllText(cache)) : new ZomboidFeedDoc();
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
		{
			return new ZomboidFeedDoc();
		}
	}
}
