using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

public sealed class LauncherFoundationTests
{
    [Fact]
    public void FailedDecryptionPreservesTheOriginalSession()
    {
        string root = NewDirectory();
        byte[] original = { 3, 1, 4, 1, 5 };
        string path = Path.Combine(root, "session.bin");
        File.WriteAllBytes(path, original);
        var store = new FileAuthStore(root, new FailingProtector());
        Assert.ThrowsAny<IOException>(() => store.Read("session.bin"));
        Assert.True(File.Exists(path));
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.ThrowsAny<IOException>(() => store.Read("session.bin"));
        Assert.Equal(original, File.ReadAllBytes(path));
        store.Delete("session.bin");
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("session.bin", false)]
    [InlineData("msal.bin", false)]
    [InlineData("session.bin", true)]
    [InlineData("msal.bin", true)]
    public async Task UnreadableSavedCredentialsDoNotStartSignInOrEraseOriginals(string name, bool explicitSignIn)
    {
        string root = NewDirectory();
        byte[] original = { 7, 8, 9 };
        File.WriteAllBytes(Path.Combine(root, name), original);
        var store = new FileAuthStore(root, new FailingProtector());
        var microsoft = new NoAccountCalls();
        using var http = new HttpClient(new NoNetwork());
        var signIn = new NorcomSignIn(microsoft, new MinecraftServicesAuth(http), store, root,
            () => "11111111-2222-3333-4444-555555555555");
        var result = explicitSignIn
            ? await signIn.SignInAsync(MicrosoftPrompt.SystemBrowser, null, CancellationToken.None)
            : await signIn.ForPlayAsync(CancellationToken.None);
        Assert.Equal("StorageUnavailable", result.Failure.ToString());
        Assert.Equal(0, microsoft.Calls);
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(root, name)));
    }

    [Fact]
    public void PassiveRestoreReportsUnreadableStateWithoutThrowingOrErasingIt()
    {
        string root = NewDirectory();
        File.WriteAllBytes(Path.Combine(root, "session.bin"), new byte[] { 1 });
        using var http = new HttpClient(new NoNetwork());
        var signIn = new NorcomSignIn(new NoAccountCalls(), new MinecraftServicesAuth(http),
            new FileAuthStore(root, new FailingProtector()), root,
            () => "11111111-2222-3333-4444-555555555555");
        var result = signIn.RestoreStored();
        Assert.False(result.SignedIn);
        Assert.Contains("saved sign-in", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(root, "session.bin")));
    }

    [Theory]
    [InlineData("self-update-2.0.44")]
    [InlineData("install-1.0.35")]
    public void OrdinaryCleanupRetainsRecoveryBytes(string directory)
    {
        string root = NewDirectory();
        string home = Path.Combine(root, "NORCOM");
        string live = Path.Combine(home, "Launcher", "NorcomLauncher.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(live)!);
        File.WriteAllText(live, "current");
        string backup = Path.Combine(home, "recovery", directory, "NorcomLauncher.previous.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        File.WriteAllText(backup, "last-known-good");
        LauncherCleanup.RemoveObsolete(live, home, Path.Combine(root, "game"), Path.Combine(root, "temp"));
        Assert.True(File.Exists(backup));
        Assert.Equal("last-known-good", File.ReadAllText(backup));
        Assert.Equal("current", File.ReadAllText(live));
    }

    [Fact]
    public void CacheWriteCannotReplaceAnUnreadableOriginal()
    {
        string root = NewDirectory();
        string file = Path.Combine(root, "msal.bin");
        byte[] original = { 9, 2, 6 };
        File.WriteAllBytes(file, original);
        var store = new FileAuthStore(root, new FailingProtector());
        Assert.ThrowsAny<IOException>(() => store.Write("msal.bin", new byte[] { 5 }));
        Assert.Equal(original, File.ReadAllBytes(file));
    }

    private static string NewDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "norcom-foundation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FailingProtector : IDataProtector
    {
        public byte[] Protect(byte[] plaintext) => plaintext;
        public byte[] Unprotect(byte[] ciphertext) => throw new InvalidOperationException("Fixture unprotect failure");
    }

    private sealed class NoAccountCalls : IMicrosoftAccount
    {
        public int Calls;
        public Task<MicrosoftAccountResult> AcquireAsync(string clientId, MicrosoftPrompt prompt,
            Action<DeviceCodePrompt>? code, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(MicrosoftAccountResult.Fail(AuthFailure.Cancelled));
        }
        public Task SignOutAsync(string clientId, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new InvalidOperationException("No auth network request permitted");
    }
}
