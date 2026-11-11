namespace UltraGearInput.Linux.Hardware;

internal static class PathResolver
{
    private const int MaximumLinks = 40;

    public static string RealPath(string path)
    {
        var links = 0;
        return Resolve(Path.GetFullPath(path), ref links);
    }

    private static string Resolve(string absolutePath, ref int links)
    {
        var current = Path.GetPathRoot(absolutePath)!;
        var parts = absolutePath[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part == ".")
            {
                continue;
            }
            if (part == "..")
            {
                current = Path.GetDirectoryName(current) ?? current;
                continue;
            }
            var candidate = Path.Combine(current, part);
            var target = new FileInfo(candidate).LinkTarget;
            if (target is null)
            {
                current = candidate;
                continue;
            }
            if (++links > MaximumLinks)
            {
                throw new IOException($"Too many symbolic links while resolving {absolutePath}.");
            }
            current = Resolve(Path.IsPathRooted(target) ? target : Path.Combine(current, target), ref links);
        }
        return current;
    }
}
