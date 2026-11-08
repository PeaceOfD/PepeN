namespace v2rayN.WebAPI.Services;

/// <summary>
/// PattN: the second move of a folder swap, which puts a folder in the place of one that was moved aside. Utils.GetBinPath
/// and Utils.GetConfigPath make the folder they are asked about whenever it is missing, so a request served between the
/// two moves can leave an empty folder in that place, which Directory.Move does not replace.
/// </summary>
internal static class DirectorySwap
{
    /// <summary>
    /// Moves <paramref name="from" /> to <paramref name="to" />, which is missing or an empty folder that appeared meanwhile
    /// (that one is removed first). Throws, and leaves <paramref name="from" /> where it is, when <paramref name="to" />
    /// holds anything.
    /// </summary>
    public static void MoveIntoPlace(string from, string to)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Move(from, to);
                return;
            }
            catch (IOException) when (attempt < 3 && IsEmptyFolder(to))
            {
                Directory.Delete(to);
            }
        }
    }

    private static bool IsEmptyFolder(string path)
    {
        try
        {
            return Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
