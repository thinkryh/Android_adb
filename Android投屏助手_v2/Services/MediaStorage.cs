using System.IO;

namespace Android投屏助手.Services;

internal static class MediaStorage
{
    public static string GetFolder()
    {
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(pictures))
            pictures = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures");
        var folder = Path.Combine(pictures, "Android投屏助手");
        Directory.CreateDirectory(folder);
        return folder;
    }
}
