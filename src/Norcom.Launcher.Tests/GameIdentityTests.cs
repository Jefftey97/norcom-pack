using System.Diagnostics;
using Norcom.Launcher.Core.Services;
using Norcom.Launcher.Core.Updates;
using Xunit;
namespace Norcom.Launcher.Tests;
public class GameIdentityTests
{
    [Fact] public void StopWithoutAnExactProcessReceiptIsNotConfirmed()
        =>Assert.False(LaunchedGame.TryStop(int.MaxValue));
    [Fact] public void RecordedGameLockKeepsTheActualProcessStartIdentity()
    {
        var path=Path.Combine(Path.GetTempPath(),"game-identity-"+Guid.NewGuid().ToString("N"));
        try
        {
            using var process=Process.GetCurrentProcess();StartupHeal.WriteGameLock(path,process.Id);
            Assert.Contains("processStartedUtcTicks="+process.StartTime.ToUniversalTime().Ticks,File.ReadAllText(path));
        }
        finally{if(File.Exists(path))File.Delete(path);}
    }
    [Fact] public void ReusedPidLockCanBeClearedWithoutStoppingThatProcess()
    {
        var path=Path.Combine(Path.GetTempPath(),"game-reused-"+Guid.NewGuid().ToString("N"));
        try
        {
            using var process=Process.GetCurrentProcess();File.WriteAllText(path,$"pid={process.Id}\nprocessStartedUtcTicks=1\n");
            Assert.False(GameUpdateGuard.IsBlocked(path));Assert.False(process.HasExited);Assert.False(File.Exists(path));
        }
        finally{if(File.Exists(path))File.Delete(path);}
    }
}
