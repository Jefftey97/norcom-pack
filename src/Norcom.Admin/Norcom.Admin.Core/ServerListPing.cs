using System.Net.Sockets;
using System.Text;

namespace Norcom.Admin.Core;

public static class ServerListPing
{
    public static async Task<bool> TryAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(host, "127.0.0.1", StringComparison.Ordinal) || port is < 1 or > 65535)
        {
            return false;
        }

        try
        {
            using var tcp = new TcpClient();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(timeout);
            await tcp.ConnectAsync(host, port, linked.Token).ConfigureAwait(false);
            await using var stream = tcp.GetStream();
            var handshake = Handshake(host, port);
            await stream.WriteAsync(handshake, linked.Token).ConfigureAwait(false);
            await stream.WriteAsync(new byte[] { 1, 0 }, linked.Token).ConfigureAwait(false);
            var length = await ReadVarIntAsync(stream, linked.Token).ConfigureAwait(false);
            if (length is < 2 or > 65536)
            {
                return false;
            }

            var body = await ReadExactAsync(stream, length, linked.Token).ConfigureAwait(false);
            var text = Encoding.UTF8.GetString(body);
            return text.Contains("\"players\"", StringComparison.Ordinal) || text.Contains("\"version\"", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    public static async Task<int?> PlayersAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(host, "127.0.0.1", StringComparison.Ordinal) || port is < 1 or > 65535)
        {
            return null;
        }

        try
        {
            using var tcp = new TcpClient();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(timeout);
            await tcp.ConnectAsync(host, port, linked.Token).ConfigureAwait(false);
            await using var stream = tcp.GetStream();
            await stream.WriteAsync(Handshake(host, port), linked.Token).ConfigureAwait(false);
            await stream.WriteAsync(new byte[] { 1, 0 }, linked.Token).ConfigureAwait(false);
            var length = await ReadVarIntAsync(stream, linked.Token).ConfigureAwait(false);
            if (length is < 2 or > 65536)
            {
                return null;
            }

            var text = Encoding.UTF8.GetString(await ReadExactAsync(stream, length, linked.Token).ConfigureAwait(false));
            var match = System.Text.RegularExpressions.Regex.Match(text, "\"online\"\\s*:\\s*(\\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value) : null;
        }
        catch
        {
            return null;
        }
    }

    private static byte[] Handshake(string host, int port)
    {
        var payload = new List<byte> { 0 };
        WriteVarInt(payload, 767);
        var hostBytes = Encoding.UTF8.GetBytes(host);
        WriteVarInt(payload, hostBytes.Length);
        payload.AddRange(hostBytes);
        payload.Add((byte)(port >> 8));
        payload.Add((byte)port);
        payload.Add(1);
        var packet = new List<byte>();
        WriteVarInt(packet, payload.Count);
        packet.AddRange(payload);
        return packet.ToArray();
    }

    private static void WriteVarInt(List<byte> buffer, int value)
    {
        var current = (uint)value;
        do
        {
            var next = (byte)(current & 0x7F);
            current >>= 7;
            if (current != 0)
            {
                next |= 0x80;
            }

            buffer.Add(next);
        }
        while (current != 0);
    }

    private static async Task<int> ReadVarIntAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var value = 0;
        var shift = 0;
        for (var i = 0; i < 5; i++)
        {
            var one = await ReadExactAsync(stream, 1, cancellationToken).ConfigureAwait(false);
            value |= (one[0] & 0x7F) << shift;
            if ((one[0] & 0x80) == 0)
            {
                return value;
            }

            shift += 7;
        }

        return -1;
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        var read = 0;
        while (read < count)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, count - read), cancellationToken).ConfigureAwait(false);
            if (n == 0)
            {
                throw new EndOfStreamException();
            }

            read += n;
        }

        return buffer;
    }
}
