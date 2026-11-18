using System.Xml;
using System.Xml.Linq;
using System.Runtime.Versioning;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace MuhasebeTakip2.App.Services;

public static class PersistentSessionKeys
{
    public static void AddPersistentSessionKeys(this WebApplicationBuilder builder)
    {
        // Opt-in only, after existing keys have been securely copied and verified.
        if (!builder.Configuration.GetValue<bool>("DataProtection:PersistKeys")) return;
        if (!OperatingSystem.IsLinux())
            throw new InvalidOperationException("Persistent session keys require the reviewed Linux storage setup.");
        var path = Path.Combine(CompanyDiskStorage.Root(builder.Environment), "data-protection");
        if (!Directory.Exists(path) || !Directory.EnumerateFiles(path, "*.xml").Any())
            throw new InvalidOperationException("Existing session keys must be securely staged before activation.");
        if (builder.Environment.IsProduction())
        {
            var uploads = Path.GetFullPath(Path.Combine(builder.Environment.WebRootPath, "uploads"));
            var mountPath = uploads.Replace("\\", "\\134").Replace(" ", "\\040").Replace("\t", "\\011");
            if (!File.ReadLines("/proc/self/mountinfo").Any(line => line.Split(' ') is var fields && fields.Length > 4 && fields[4] == mountPath))
                throw new InvalidOperationException("Persistent session key disk is not mounted.");
        }
        var repository = new PrivateKeyRepository(path, requireExistingKeys: true);
        _ = repository.GetAllElements(); // Fail closed before serving requests on malformed/unsafe keys.
        // Do not change ApplicationDiscriminator, purposes or the cookie name:
        // legacy cookies must retain the same cryptographic identity.
        builder.Services.AddDataProtection().AddKeyManagementOptions(options => options.XmlRepository = repository);
    }
}

/// <summary>Owner-only, atomic XML key storage. Never deletes or replaces keys.</summary>
[SupportedOSPlatform("linux")]
public sealed class PrivateKeyRepository : IXmlRepository
{
    private readonly string directory;
    private readonly bool requireExistingKeys;
    private const UnixFileMode DirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode FileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public PrivateKeyRepository(string directory, bool requireExistingKeys = false)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        this.directory = Path.GetFullPath(directory);
        this.requireExistingKeys = requireExistingKeys;
        CompanyDiskStorage.EnsurePrivateDirectory(this.directory);
        ValidateDirectory();
    }

    private void ValidateDirectory()
    {
        CompanyDiskStorage.VerifyDirectoryPath(directory);
        if (File.GetUnixFileMode(directory) != DirectoryMode)
            throw new IOException("Session key directory permissions are unsafe.");
    }

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        ValidateDirectory();
        var elements = new List<XElement>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.xml"))
        {
            var info = new FileInfo(path);
            if ((info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0 ||
                info.Length is <= 0 or > 1024 * 1024 || File.GetUnixFileMode(path) != FileMode)
                throw new IOException("Session key file is unsafe.");
            using var input = File.OpenRead(path);
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024
            });
            elements.Add(XElement.Load(reader));
        }
        if (requireExistingKeys && !elements.Any(x => x.Name == "key" && Guid.TryParse(x.Attribute("id")?.Value, out _)))
            throw new IOException("Existing session key ring is missing; automatic replacement is not allowed.");
        return elements;
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        ValidateDirectory();
        // No caller-controlled path; include revocations as well as keys.
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".xml");
        var temporary = path + ".tmp";
        var created = false;
        try
        {
            using (var output = new FileStream(temporary, new FileStreamOptions
            {
                Mode = System.IO.FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
                UnixCreateMode = FileMode
            }))
            {
                created = true;
                element.Save(output);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: false);
        }
        finally
        {
            if (created && File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
