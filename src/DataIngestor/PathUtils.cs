public static class PathUtils
{
    public static string FindAncestorDirectoryContaining(string pattern)
    {
        var currentDir = Directory.GetCurrentDirectory();
        while (currentDir != null)
        {
            if (Directory.GetFiles(currentDir, pattern).Any())
            {
                return currentDir!;
            }
            currentDir = Directory.GetParent(currentDir)?.FullName;
        }

        throw new FileNotFoundException($"Could not find a directory containing {pattern}");
    }
}
