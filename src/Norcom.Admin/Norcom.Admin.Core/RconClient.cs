using System.Net.Sockets;

namespace Norcom.Admin.Core;

public sealed class RconClient
{
    public async Task<string> CommandAsync(string host, int port, string password, string command, CancellationToken cancellationToken)
    {
        if (!string.Equals(host, "127.0.0.1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("RCON stays on 127.0.0.1.");
        }

        try
        {
            using var tcp = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            await tcp.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
            await using var stream = tcp.GetStream();
            await SendAsync(stream, 1, RconPacket.Auth, password, timeout.Token).ConfigureAwait(false);
            if ((await ReadAsync(stream, timeout.Token).ConfigureAwait(false)).Id == -1)
            {
                throw new InvalidOperationException("RCON authentication failed.");
            }

            await SendAsync(stream, 2, RconPacket.Command, command, timeout.Token).ConfigureAwait(false);
            return (await ReadAsync(stream, timeout.Token).ConfigureAwait(false)).Body;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("RCON request failed. " + ex.GetType().Name);
        }
    }

    private static async Task SendAsync(NetworkStream stream, int id, int type, string payload, CancellationToken cancellationToken)
    {
        var bytes = RconPacket.Build(id, type, payload);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(int Id, int Type, string Body)> ReadAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var header = await ReadExactAsync(stream, 4, cancellationToken).ConfigureAwait(false);
        var length = header[0] | (header[1] << 8) | (header[2] << 16) | (header[3] << 24);
        if (length < 10 || length > 4110)
        {
            throw new InvalidDataException("RCON length is not usable.");
        }

        return RconPacket.Parse(await ReadExactAsync(stream, length, cancellationToken).ConfigureAwait(false));
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
