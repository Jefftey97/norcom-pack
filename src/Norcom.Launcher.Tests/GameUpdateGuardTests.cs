using Norcom.Launcher.Core.Updates;
using Xunit;

namespace Norcom.Launcher.Tests;

public sealed class GameUpdateGuardTests:IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"norcom-game-guard-"+Guid.NewGuid().ToString("N"));
    public GameUpdateGuardTests()=>Directory.CreateDirectory(root);
    [Fact] public void OnlyAbsentOrDemonstrablyDeadGameLocksPermitUpdating()
    {
        var path=Path.Combine(root,"game.lock");Assert.False(GameUpdateGuard.IsBlocked(path));
        File.WriteAllText(path,"pid="+Environment.ProcessId+"\nkind=game\n");
        Assert.True(GameUpdateGuard.IsBlocked(path));Assert.True(File.Exists(path));
        File.WriteAllText(path,"pid="+int.MaxValue+"\nkind=game\n");
        Assert.False(GameUpdateGuard.IsBlocked(path));Assert.False(File.Exists(path));
    }
    [Theory][InlineData("pid=bad")][InlineData("pid=0")][InlineData("pid=2147483647\npid=1")][InlineData("")]
    public void UnknownGameLockIsKeptAndBlocksUpdate(string value)
    {
        var path=Path.Combine(root,"game.lock");File.WriteAllText(path,value);
        Assert.True(GameUpdateGuard.IsBlocked(path));Assert.Equal(value,File.ReadAllText(path));
    }
    [Fact] public void UnreadableLockIsKeptAndBlocksUpdate()
    {
        var path=Path.Combine(root,"game.lock");File.WriteAllText(path,"pid=2147483647");
        using var locked=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        Assert.True(GameUpdateGuard.IsBlocked(path));Assert.True(File.Exists(path));
    }
    [Theory][InlineData("pid=bad")][InlineData("pid=2147483647\npid=1")][InlineData("")]
    public void RepairCleanupPreservesUncertainGameLocks(string value)
    {
        var path=Path.Combine(root,"game.lock");File.WriteAllText(path,value);
        Norcom.Launcher.Core.Services.StartupHeal.ClearLockIfDead(path);
        Assert.True(File.Exists(path));Assert.Equal(value,File.ReadAllText(path));
    }
    public void Dispose()=>Directory.Delete(root,true);
}
