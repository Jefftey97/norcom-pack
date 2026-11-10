using System;
using System.IO;

namespace Norcom.Launcher.Core.Services;

public sealed class ProtectedTokenCache
{
    private readonly IAuthSecretStore _store;
    private bool _readable;
    public ProtectedTokenCache(IAuthSecretStore store) => _store = store;

    public void BeforeAccess(Action<byte[]> deserialize)
    {
        _readable = false;
        try
        {
            byte[] bytes = _store.Read("msal.bin");
            if (bytes != null)
            {
                if (bytes.Length == 0) throw new InvalidDataException("Empty saved cache");
                deserialize(bytes);
            }
            _readable = true;
        }
        catch (AuthStoreUnavailableException) { throw; }
        catch (Exception ex) { throw new AuthStoreUnavailableException(ex); }
    }

    public void AfterAccess(bool changed, Func<byte[]> serialize)
    {
        if (!changed) return;
        if (!_readable)
            throw new AuthStoreUnavailableException(new InvalidDataException("Saved cache was not read"));
        try { _store.Write("msal.bin", serialize()); }
        catch (AuthStoreUnavailableException) { throw; }
        catch (Exception ex) { throw new AuthStoreUnavailableException(ex); }
    }
}
