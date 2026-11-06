namespace Norcom.Admin.Core;

public static class SharedFile
{
    public static IEnumerable<string> ReadLines(string path)
    {
        using var stream = Open(path);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    public static string ReadText(string path)
    {
        using var stream = Open(path);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static FileStream Open(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
}
