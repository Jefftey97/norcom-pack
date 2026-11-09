#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Norcom.Launcher.Core.Updates;

public static class GameUpdateGuard
{
    public static bool IsBlocked(string path)
    {
        try
        {
            try{File.GetAttributes(path);}
            catch(FileNotFoundException){return false;}
            catch(DirectoryNotFoundException){return false;}
            OwnedFiles.CheckAncestors(path);
            if(new FileInfo(path).Length>8192)return true;
            var original=File.ReadAllText(path);
            var lines=original.Split('\n').Select(x=>x.Trim()).Where(x=>x.StartsWith("pid=",StringComparison.Ordinal)).ToArray();
            if(lines.Length!=1 || !int.TryParse(lines[0].Substring(4),out var pid) || pid<=0)return true;
            try
            {
                using var process=Process.GetProcessById(pid);
                if(!process.HasExited)return true; // Includes uncertain/reused PID: never stop it.
            }
            catch(ArgumentException) { }
            if(File.ReadAllText(path)!=original)return true;
            File.Delete(path);
            return false;
        }
        catch(IOException){return true;}
        catch(UnauthorizedAccessException){return true;}
        catch(System.ComponentModel.Win32Exception){return true;}
    }
}
