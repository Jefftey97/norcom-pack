using System;
namespace Norcom.Launcher.Core.Services;
public sealed record WindowBounds(double Width,double Height,double MinWidth,double MinHeight);
public static class WindowSizing
{
    public static WindowBounds Fit(double requestedWidth,double requestedHeight,double availableWidth,double availableHeight)
    {
        var width=double.IsFinite(availableWidth)?Math.Max(1,availableWidth):1180;
        var height=double.IsFinite(availableHeight)?Math.Max(1,availableHeight):800;
        var minWidth=Math.Min(560,width);var minHeight=Math.Min(420,height);
        return new WindowBounds(Math.Clamp(requestedWidth,minWidth,width),Math.Clamp(requestedHeight,minHeight,height),minWidth,minHeight);
    }
}
