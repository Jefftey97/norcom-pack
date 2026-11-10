using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Norcom.Launcher.Core.Updates;

// Owner-side release tool. This assembly is never included in friends' packages.
if(args.Length<3 || (args[0]!="init" && args[0]!="sign"))
{
    Console.Error.WriteLine("Usage: init PRIVATE_DIRECTORY PUBLIC_TRUST_JSON | sign PRIVATE_DIRECTORY PAYLOAD_JSON ENVELOPE_JSON");return 2;
}
try
{
    var directory=Path.GetFullPath(args[1]);
    RequirePrivateDirectory(directory);
    var privatePath=Path.Combine(directory,"release-key.dpapi");
    var publicPath=Path.Combine(directory,"release-public.json");
    if(File.Exists(privatePath))RequirePrivateFile(privatePath);
    if(File.Exists(publicPath))RequirePrivateFile(publicPath);
    var entropy=Encoding.UTF8.GetBytes("NORCOM Launcher release signing identity v1");
    using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
    if(args[0]=="init" && !File.Exists(privatePath))
    {
        if(File.Exists(publicPath))throw new InvalidOperationException("Public identity exists without its private key; do not replace it.");
        if(File.Exists(args[2]) && JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(args[2]))?.Count!=0)
            throw new InvalidOperationException("A public trust root already exists; initialization cannot rotate it.");
        byte[] plain=key.ExportPkcs8PrivateKey();
        try
        {
            var protectedBytes=ProtectedData.Protect(plain,entropy,DataProtectionScope.CurrentUser);
            using var output=new FileStream(privatePath,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough);
            output.Write(protectedBytes);output.Flush(true);
        }
        finally{CryptographicOperations.ZeroMemory(plain);}
    }
    else if(!File.Exists(privatePath))throw new FileNotFoundException("The established signing identity is missing.");
    var decrypted=ProtectedData.Unprotect(File.ReadAllBytes(privatePath),entropy,DataProtectionScope.CurrentUser);
    try
    {
        key.ImportPkcs8PrivateKey(decrypted,out var read);
        if(read!=decrypted.Length)throw new InvalidDataException("Invalid private key encoding.");
    }
    finally{CryptographicOperations.ZeroMemory(decrypted);}
    if(key.KeySize!=256 || key.ExportParameters(false).Curve.Oid.Value!="1.2.840.10045.3.1.7")
        throw new CryptographicException("Signing identity must use the pinned P-256 curve.");
    RequirePrivateFile(privatePath);
    var publicPem=key.ExportSubjectPublicKeyInfoPem();
    var fingerprint=Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
    var keyId="norcom-launcher-"+fingerprint[..16];
    var trust=new Dictionary<string,string>{{keyId,publicPem}};
    var trustJson=JsonSerializer.Serialize(trust,new JsonSerializerOptions{WriteIndented=true})+"\n";
    if(File.Exists(publicPath) && File.ReadAllText(publicPath)!=trustJson)
        throw new InvalidDataException("The private key does not match the recorded public identity.");
    if(!File.Exists(publicPath))WriteNew(publicPath,Encoding.UTF8.GetBytes(trustJson));
    var probe=Encoding.UTF8.GetBytes("NORCOM release signing self-check");
    var signature=key.SignData(probe,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    using var publicOnly=ECDsa.Create();publicOnly.ImportFromPem(publicPem);
    if(!publicOnly.VerifyData(probe,signature,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        throw new CryptographicException("Signing identity self-check failed.");
    if(args[0]=="init")
    {
        if(File.Exists(args[2]))
        {
            var existing=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(args[2]))!;
            if(existing.Count!=0 && (existing.Count!=1 || !existing.TryGetValue(keyId,out var pem) || pem!=publicPem))
                throw new InvalidDataException("Pinned public identity does not match; refusing key rotation.");
        }
        File.WriteAllText(args[2],trustJson,new UTF8Encoding(false));
        Console.WriteLine("Public signing identity initialized and verified: "+fingerprint);
    }
    else
    {
        if(args.Length!=4)throw new ArgumentException("Signing requires exact payload and output paths.");
        var bytes=File.ReadAllBytes(args[2]);
        signature=key.SignData(ReleaseVerifier.Domain.Concat(bytes).ToArray(),HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var envelope=JsonSerializer.Serialize(new{format="norcom-release-v1",keyId,payload=Convert.ToBase64String(bytes),signature=Convert.ToBase64String(signature)});
        new ReleaseVerifier(trust).Verify(envelope,DateTimeOffset.UtcNow);
        WriteNew(args[3],Encoding.UTF8.GetBytes(envelope));
        Console.WriteLine("Signed and independently verified the release envelope.");
    }
    return 0;
}
catch(Exception ex){Console.Error.WriteLine(ex.GetType().Name+": "+ex.Message);return 1;}

static void WriteNew(string path,byte[] bytes)
{
    using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough);
    file.Write(bytes);file.Flush(true);
}
static void RequirePrivateDirectory(string directory)
{
    if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Signing identity requires Windows user-scoped protection.");
    var info=new DirectoryInfo(directory);
    if(!info.Exists || (info.Attributes & FileAttributes.ReparsePoint)!=0)throw new IOException("An existing private, unlinked signing directory is required.");
    CheckAncestors(directory);
    var me=WindowsIdentity.GetCurrent().User?.Value??throw new IOException("Windows owner identity is unavailable.");
    var acl=info.GetAccessControl();
    if(!acl.AreAccessRulesProtected)throw new IOException("Signing directory must have explicit private permissions.");
    CheckAcl(acl,me);
}
static void RequirePrivateFile(string path)
{
    CheckAncestors(path);
    var me=WindowsIdentity.GetCurrent().User?.Value??throw new IOException("Windows owner identity is unavailable.");
    CheckAcl(new FileInfo(path).GetAccessControl(),me);
}
static void CheckAcl(FileSystemSecurity acl,string me)
{
    if(acl.GetOwner(typeof(SecurityIdentifier)).Value!=me)throw new IOException("Signing material must be owned by the current Windows user.");
    foreach(FileSystemAccessRule rule in acl.GetAccessRules(true,true,typeof(SecurityIdentifier)))
        if(rule.AccessControlType==AccessControlType.Allow && rule.IdentityReference.Value!=me && rule.IdentityReference.Value!="S-1-5-18")
            throw new IOException("Signing directory grants access to an unexpected identity.");
}
static void CheckAncestors(string path)
{
    for(var current=Path.GetFullPath(path);current!=null;current=Path.GetDirectoryName(current))
        if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("Signing paths must not contain links.");
}
