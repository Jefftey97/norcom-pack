namespace Norcom.Admin.Core;

public static class LogTail
{
    public static string ReadFrom(string path, ref long position, SecretRedactor redactor)
    {
        if (!File.Exists(path))
        {
            return "";
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (position < 0 || position > stream.Length)
        {
            position = Math.Max(0, stream.Length - 16000);
        }

        stream.Position = position;
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        position = stream.Position;
        return redactor.Clean(text);
    }
}
