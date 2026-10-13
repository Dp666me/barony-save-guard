namespace BaronySaveGuard;

public static class SaveFiles
{
    public const string Extension = ".baronysave";

    public static bool IsSaveFile(string path) =>
        path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads a file the game may still be writing. Returns null if it vanished or stayed locked.
    /// </summary>
    public static byte[]? TryRead(string path, int attempts = 5)
    {
        for (var i = 0; i < attempts; i++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var ms = new MemoryStream();
                fs.CopyTo(ms);
                return ms.ToArray();
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            catch (IOException) { Thread.Sleep(200); }
            catch (UnauthorizedAccessException) { Thread.Sleep(200); }
        }
        return null;
    }

    /// <summary>
    /// Writes <paramref name="content"/> to <paramref name="path"/> via a temp file.
    /// With <paramref name="overwrite"/> false, it never replaces a file that appeared meanwhile.
    /// </summary>
    public static bool WriteAtomic(string path, byte[] content, bool overwrite)
    {
        var tmp = Path.Combine(Path.GetDirectoryName(path)!, "." + Path.GetFileName(path) + ".bsg-tmp");
        File.WriteAllBytes(tmp, content);
        try
        {
            File.Move(tmp, path, overwrite);
            return true;
        }
        catch (IOException) when (!overwrite && File.Exists(path))
        {
            File.Delete(tmp);
            return false;
        }
    }
}
