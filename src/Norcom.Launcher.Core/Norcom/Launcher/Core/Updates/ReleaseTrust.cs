#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Norcom.Launcher.Core.Updates;

public static class ReleaseTrust
{
    public static ReleaseVerifier CreateVerifier()
    {
        using var stream=typeof(ReleaseTrust).Assembly.GetManifestResourceStream("Norcom.Launcher.ReleaseTrust.json")
            ??throw new InvalidDataException("This build has no launcher update trust root.");
        var keys=JsonSerializer.Deserialize<Dictionary<string,string>>(stream)
            ??throw new InvalidDataException("This build has invalid launcher update trust.");
        if(keys.Count<1)throw new InvalidDataException("This build has no configured launcher update signing identity.");
        foreach(var key in keys)
            if(key.Key.StartsWith("test",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Test keys cannot authorize launcher releases.");
        return new ReleaseVerifier(keys);
    }
}
