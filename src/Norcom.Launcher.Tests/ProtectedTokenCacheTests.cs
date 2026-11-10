using Norcom.Launcher.Core.Services;
using Xunit;

namespace Norcom.Launcher.Tests;

public sealed class ProtectedTokenCacheTests
{
    [Fact]
    public void EmptySavedCacheIsUnavailableAndCannotBeOverwritten()
    {
        var store = new MemoryStore { Bytes = Array.Empty<byte>() };
        var cache = new ProtectedTokenCache(store);
        Assert.Throws<AuthStoreUnavailableException>(() => cache.BeforeAccess(_ => { }));
        Assert.Throws<AuthStoreUnavailableException>(() => cache.AfterAccess(true, () => new byte[] { 3 }));
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public void DecodeFailureNeverAuthorizesCacheReplacement()
    {
        var store = new MemoryStore { Bytes = new byte[] { 1, 2, 3 } };
        var cache = new ProtectedTokenCache(store);
        Assert.Throws<AuthStoreUnavailableException>(() => cache.BeforeAccess(_ => throw new InvalidDataException()));
        Assert.Throws<AuthStoreUnavailableException>(() => cache.AfterAccess(true, () => new byte[] { 9 }));
        Assert.Equal(0, store.Writes);
        Assert.Equal(new byte[] { 1, 2, 3 }, store.Bytes);
    }

    [Fact]
    public void OnlySuccessfulReadAllowsAChangedCacheWrite()
    {
        var store = new MemoryStore();
        var cache = new ProtectedTokenCache(store);
        Assert.Throws<AuthStoreUnavailableException>(() => cache.AfterAccess(true, () => new byte[] { 1 }));
        cache.BeforeAccess(_ => throw new InvalidOperationException("Absent cache must not be decoded"));
        cache.AfterAccess(false, () => throw new InvalidOperationException("Unchanged cache must not be serialized"));
        Assert.Equal(0, store.Writes);
        cache.AfterAccess(true, () => new byte[] { 2 });
        Assert.Equal(new byte[] { 2 }, store.Bytes);
        byte[]? decoded = null;
        cache.BeforeAccess(bytes => decoded = bytes);
        Assert.Equal(new byte[] { 2 }, decoded);
        cache.AfterAccess(true, () => new byte[] { 3 });
        Assert.Equal(2, store.Writes);
    }

    [Fact]
    public void NewFailedReadRevokesPreviousWritePermission()
    {
        var store = new MemoryStore();
        var cache = new ProtectedTokenCache(store);
        cache.BeforeAccess(_ => { });
        store.Bytes = new byte[] { 1 };
        Assert.Throws<AuthStoreUnavailableException>(() => cache.BeforeAccess(_ => throw new InvalidDataException()));
        Assert.Throws<AuthStoreUnavailableException>(() => cache.AfterAccess(true, () => new byte[] { 9 }));
        Assert.Equal(0, store.Writes);
    }

    private sealed class MemoryStore : IAuthSecretStore
    {
        public string Directory => "unused";
        public byte[]? Bytes;
        public int Writes;
        public byte[]? Read(string name) => Bytes;
        public void Write(string name, byte[] bytes) { Writes++; Bytes = bytes; }
        public void Delete(string name) => Bytes = null;
    }
}
