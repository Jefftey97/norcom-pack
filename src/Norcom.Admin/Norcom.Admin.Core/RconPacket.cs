using System.Text;

namespace Norcom.Admin.Core;

public static class RconPacket
{
    public const int Auth = 3;
    public const int Command = 2;

    public static byte[] Build(int id, int type, string payload)
    {
        var body = Encoding.UTF8.GetBytes(payload);
        var length = 8 + body.Length + 2;
        var packet = new byte[4 + length];
        WriteInt(packet, 0, length);
        WriteInt(packet, 4, id);
        WriteInt(packet, 8, type);
        body.CopyTo(packet, 12);
        return packet;
    }

    public static (int Id, int Type, string Body) Parse(byte[] packet)
    {
        if (packet.Length < 10)
        {
            throw new InvalidDataException("RCON packet is too short.");
        }

        var id = ReadInt(packet, 0);
        var type = ReadInt(packet, 4);
        var body = Encoding.UTF8.GetString(packet, 8, Math.Max(0, packet.Length - 10)).TrimEnd('\0');
        return (id, type, body);
    }

    private static void WriteInt(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }

    private static int ReadInt(byte[] buffer, int offset) =>
        buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24);
}
