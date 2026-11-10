using System;
using System.IO;

namespace Norcom.Launcher.Core.Services;

public sealed class AuthStoreUnavailableException : IOException
{
    public AuthStoreUnavailableException(Exception inner)
        : base(AuthMessages.StorageUnavailable, inner) { }
}
