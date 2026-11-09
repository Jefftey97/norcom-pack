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

    public static async Task<ServerStatus> PingAsync(string host,int port,int protocol,CancellationToken ct)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromMilliseconds(2500));
        var token=deadline.Token;
        try
        {
            using var tcp=new TcpClient();await tcp.ConnectAsync(host,port,token).ConfigureAwait(false);
            using var stream=tcp.GetStream();var watch=Stopwatch.StartNew();
            await stream.WriteAsync(BuildHandshake(host,port,protocol),token).ConfigureAwait(false);
            await stream.WriteAsync(new byte[]{1,0},token).ConfigureAwait(false);
            var length=await ReadVarIntAsync(stream,token).ConfigureAwait(false);
            if(length<2 || length>1048576)throw new InvalidDataException("Status frame size is invalid.");
            var bytes=new byte[length];await stream.ReadExactlyAsync(bytes,token).ConfigureAwait(false);
            using var packet=new MemoryStream(bytes,false);
            if(await ReadVarIntAsync(packet,token).ConfigureAwait(false)!=0)throw new InvalidDataException("Unexpected status response.");
            var textLength=await ReadVarIntAsync(packet,token).ConfigureAwait(false);
            if(textLength<2 || textLength>packet.Length-packet.Position)throw new InvalidDataException("Status text size is invalid.");
            var jsonBytes=new byte[textLength];await packet.ReadExactlyAsync(jsonBytes,token).ConfigureAwait(false);
            using var doc=JsonDocument.Parse(new UTF8Encoding(false,true).GetString(jsonBytes),new JsonDocumentOptions{MaxDepth=32});
            var root=doc.RootElement;var players=0;var max=0;var names=new List<string>();
            if(root.TryGetProperty("players",out var population) && population.ValueKind==JsonValueKind.Object)
            {
                if(population.TryGetProperty("online",out var online) && !online.TryGetInt32(out players))throw new InvalidDataException("Invalid player count.");
                if(population.TryGetProperty("max",out var capacity) && !capacity.TryGetInt32(out max))throw new InvalidDataException("Invalid capacity.");
                if(players<0 || max<0)throw new InvalidDataException("Invalid player count.");
                if(population.TryGetProperty("sample",out var sample) && sample.ValueKind==JsonValueKind.Array)
                    foreach(var item in sample.EnumerateArray())
                    {
                        if(names.Count>=100)break;
                        if(item.ValueKind==JsonValueKind.Object && item.TryGetProperty("name",out var name) && name.ValueKind==JsonValueKind.String)names.Add(name.GetString()??"");
                    }
            }
            var motd=root.TryGetProperty("description",out var description)?Strip(Flatten(description)):"";
            return new ServerStatus(true,players,max,motd,(int)watch.ElapsedMilliseconds,names,"");
        }
        catch(Exception ex)
        {
            return new ServerStatus(false,0,0,"",-1,Array.Empty<string>(),ex is OperationCanceledException?"Status check timed out.":"The server status could not be read.");
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

    private static async Task<int> ReadVarIntAsync(Stream stream,CancellationToken ct)
    {
        var result=0;var buffer=new byte[1];
        for(var shift=0;shift<35;shift+=7)
        {
            await stream.ReadExactlyAsync(buffer,ct).ConfigureAwait(false);var b=buffer[0];
            if(shift==28 && (b&0xf0)!=0)throw new InvalidDataException("Invalid status varint.");
            result|=(b&0x7f)<<shift;if((b&0x80)==0)return result;
        }
        throw new InvalidDataException("Invalid status varint.");
    }
}
