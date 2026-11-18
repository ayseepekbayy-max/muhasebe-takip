namespace MuhasebeTakip2.App.Services;

public static class CompanyDiskStorage
{
    public const string PrivateDirectory = ".firmova-private";
    public static string Root(IWebHostEnvironment environment) =>
        Path.Combine(environment.WebRootPath, "uploads", PrivateDirectory);

    public static string Archives(IWebHostEnvironment environment) => Path.Combine(Root(environment), "recovery");

    public static void EnsurePrivateDirectory(string path)
    {
        VerifyDirectoryPath(path);
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(path);
        else
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    internal static void VerifyDirectoryPath(string path)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(path));
        for (var ancestor = directory; ancestor is not null; ancestor = ancestor.Parent)
            if (ancestor.Exists && (ancestor.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Company storage must not contain symbolic links.");
    }
}
