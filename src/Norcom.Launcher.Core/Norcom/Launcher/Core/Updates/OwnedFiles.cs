using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Norcom.Launcher.Core.Updates;

internal static class OwnedFiles
{
    public static void CheckAncestors(string path)
    {
        for (var item = Path.GetFullPath(path); item != null; item = Path.GetDirectoryName(item))
        {
            try
            {
                if ((File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Launcher update paths cannot contain links.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
    public static void WriteNew(string path, byte[] bytes)
    {
        CheckAncestors(path);
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        file.Write(bytes);
        file.Flush(true);
    }
    public static void WriteAtomic<T>(string path, T value)
    {
        CheckAncestors(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".new";
        WriteNew(temporary, JsonSerializer.SerializeToUtf8Bytes(value));
        try
        {
            CheckAncestors(path);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static T Read<T>(string path)
    {
        CheckAncestors(path);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (input.Length > 65536) throw new InvalidDataException("Launcher update state exceeds its limit.");
        using var doc = JsonDocument.Parse(input);
        RejectDuplicateProperties(doc.RootElement);
        var resolver=new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info=>{foreach(var property in info.Properties)property.IsRequired=true;});
        var options=new JsonSerializerOptions{UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,TypeInfoResolver=resolver};
        return doc.RootElement.Deserialize<T>(options) ?? throw new InvalidDataException("Launcher update state is empty.");
    }
    private static void RejectDuplicateProperties(JsonElement element)
    {
        if(element.ValueKind==JsonValueKind.Object)
        {
            var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var property in element.EnumerateObject())
            {
                if(!seen.Add(property.Name))throw new InvalidDataException("Duplicate recovery state property.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if(element.ValueKind==JsonValueKind.Array)
            foreach(var item in element.EnumerateArray())RejectDuplicateProperties(item);
    }
}
