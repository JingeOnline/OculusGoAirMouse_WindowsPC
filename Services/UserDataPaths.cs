using System.IO;

namespace OculusGoAirMouse.Services;

public static class UserDataPaths
{
    // The executable directory stays stable even when a shortcut changes the working directory.
    public static string DirectoryPath => GetDirectory(AppContext.BaseDirectory, Environment.UserName);
    public static string GetDirectory(string applicationDirectory, string userName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string safeName = new(userName.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        safeName = safeName.Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "UnknownUser";
        return Path.Combine(Path.GetFullPath(applicationDirectory), "UserData", safeName);
    }
}
