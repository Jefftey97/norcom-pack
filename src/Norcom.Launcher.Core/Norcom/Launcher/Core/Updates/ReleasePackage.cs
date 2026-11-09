using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Norcom.Launcher.Core.Updates;

public static class ReleasePackage
{
    public static async Task DownloadAsync(HttpClient http, VerifiedRelease release, string destination, CancellationToken ct)
    {
        var p = release.Payload;
        OwnedFiles.CheckAncestors(destination);
        using var response = await http.GetAsync(p.PackageUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.StatusCode != System.Net.HttpStatusCode.OK ||
            response.RequestMessage?.RequestUri?.AbsoluteUri != p.PackageUrl ||
            (response.Content.Headers.ContentLength is long size && size != p.PackageBytes))
            throw new InvalidDataException("The launcher download response is not the signed package.");
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            await using (var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[65536];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > p.PackageBytes) throw new InvalidDataException("Launcher package exceeds its signed size.");
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                }
                if (total != p.PackageBytes || Hex(hash.GetHashAndReset()) != p.PackageSha256)
                    throw new InvalidDataException("Launcher package size or hash does not match.");
                output.Flush(true);
            }
            OwnedFiles.CheckAncestors(destination);
            File.Move(temporary, destination); // Never overwrite an existing accepted package.
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static string Extract(VerifiedRelease release, string package, string destination)
    {
        OwnedFiles.CheckAncestors(destination);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Staging directory already exists.");
        using var zip = OpenVerifiedArchive(release, package);
        var exe = zip.GetEntry("NorcomLauncher.exe");
        Directory.CreateDirectory(destination);
        OwnedFiles.CheckAncestors(destination);
        var exePath = Path.Combine(destination, "NorcomLauncher.exe");
        using (var input = exe.Open())
        using (var output = new FileStream(exePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            var buffer = new byte[65536];
            long total = 0;
            int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += count;
                if (total > release.Payload.ExecutableBytes) throw new InvalidDataException("Launcher expands beyond its signed size.");
                output.Write(buffer, 0, count);
            }
            output.Position = 0;
            if (total != release.Payload.ExecutableBytes || Hex(SHA256.HashData(output)) != release.Payload.ExecutableSha256)
                throw new InvalidDataException("Launcher executable hash does not match.");
            output.Flush(true);
        }
        OwnedFiles.WriteNew(Path.Combine(destination, "payload-version.txt"), Encoding.UTF8.GetBytes(release.Payload.Version));
        return exePath;
    }
    // A renewed envelope may reuse cached executable bytes, but it must still
    // describe the exact signed archive, including its immutable version stamp.
    public static void ValidateArchive(VerifiedRelease release, string package)
    {
        using var zip = OpenVerifiedArchive(release, package);
        using var input = zip.GetEntry("NorcomLauncher.exe").Open();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536];long total = 0;int count;
        while ((count = input.Read(buffer,0,buffer.Length)) > 0)
        {
            total += count;
            if (total > release.Payload.ExecutableBytes) throw new InvalidDataException("Launcher expands beyond its signed size.");
            hash.AppendData(buffer,0,count);
        }
        if (total != release.Payload.ExecutableBytes || Hex(hash.GetHashAndReset()) != release.Payload.ExecutableSha256)
            throw new InvalidDataException("Launcher executable hash does not match.");
    }
    private static ZipArchive OpenVerifiedArchive(VerifiedRelease release, string package)
    {
        OwnedFiles.CheckAncestors(package);
        var file = new FileStream(package, FileMode.Open, FileAccess.Read, FileShare.Read);
        ZipArchive zip = null;
        try
        {
            if (file.Length != release.Payload.PackageBytes || Hex(SHA256.HashData(file)) != release.Payload.PackageSha256)
                throw new InvalidDataException("The staged launcher package changed.");
            file.Position = 0;
            zip = new ZipArchive(file, ZipArchiveMode.Read);
            if (zip.Entries.Count != 2 || zip.Entries.Count(e => e.FullName == "NorcomLauncher.exe") != 1 ||
                zip.Entries.Count(e => e.FullName == "payload-version.txt") != 1)
                throw new InvalidDataException("Launcher package must contain exactly the executable and version stamp.");
            foreach (var entry in zip.Entries)
            {
                var kind = (entry.ExternalAttributes >> 16) & 0xF000;
                if ((kind != 0 && kind != 0x8000) || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Linked archive entries are not allowed.");
            }
            var exe = zip.GetEntry("NorcomLauncher.exe");
            var stamp = zip.GetEntry("payload-version.txt");
            if (exe.Length != release.Payload.ExecutableBytes || stamp.Length < 1 || stamp.Length > 32)
                throw new InvalidDataException("Unexpected launcher archive size.");
            using (var reader = new StreamReader(stamp.Open(), new UTF8Encoding(false, true)))
                if (reader.ReadToEnd().Trim() != release.Payload.Version) throw new InvalidDataException("Launcher version stamp disagrees with release.");
            return zip;
        }
        catch {zip?.Dispose();file.Dispose();throw;}
    }
    internal static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
