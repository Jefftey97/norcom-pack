using System.IO;

namespace Norcom.Admin.Services;

public static class DesktopShortcut
{
    public static void Ensure()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var link = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "NORCOM Admin.lnk");
            if (File.Exists(link))
            {
                return;
            }

            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath))
            {
                return;
            }

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                return;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(link);
            shortcut.TargetPath = processPath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(processPath);
            shortcut.IconLocation = processPath + ",0";
            shortcut.Description = "NORCOM Admin";
            shortcut.Save();
        }
        catch
        {
        }
    }
}
