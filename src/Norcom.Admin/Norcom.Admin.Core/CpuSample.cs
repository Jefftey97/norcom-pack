namespace Norcom.Admin.Core;

public static class CpuSample
{
    public static double Percent(TimeSpan earlierCpu, DateTime earlierWall, TimeSpan laterCpu, DateTime laterWall, int processors)
    {
        var cpu = (laterCpu - earlierCpu).TotalMilliseconds;
        var wall = (laterWall - earlierWall).TotalMilliseconds * Math.Max(1, processors);
        if (cpu < 0 || wall <= 1)
        {
            return 0;
        }

        return Math.Clamp(cpu / wall * 100.0, 0, 100);
    }
}
