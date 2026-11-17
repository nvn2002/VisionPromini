using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;

namespace TunVision.Core.Graph;

public static class PathResolver
{
    private static readonly string[] ImageExtensions =
    {
        ".png", ".bmp", ".jpg", ".jpeg", ".tif", ".tiff"
    };

    /// <summary>
    /// Returns an ordered list of candidate directories to search for images.
    /// Safely handles permission issues (e.g. UnauthorizedAccessException, SecurityException).
    /// </summary>
    public static IEnumerable<string> GetCandidateImageDirectories(string? projectDir = null, string? currentNodeFolder = null)
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddIfValid(string? d)
        {
            if (string.IsNullOrWhiteSpace(d)) return;
            try
            {
                if (Directory.Exists(d))
                {
                    dirs.Add(Path.GetFullPath(d));
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException or ArgumentException)
            {
                // Silently ignore permission-denied or invalid path directories
            }
        }

        // 1. Current node folder (if provided)
        AddIfValid(currentNodeFolder);

        // 2. Project / Preset directory and its standard subfolders
        if (!string.IsNullOrWhiteSpace(projectDir))
        {
            AddIfValid(projectDir);
            AddIfValid(Path.Combine(projectDir, "image"));
            AddIfValid(Path.Combine(projectDir, "images"));
            AddIfValid(Path.Combine(projectDir, "imgs"));
            AddIfValid(Path.Combine(projectDir, "Samples"));
            AddIfValid(Path.Combine(projectDir, "demo", "image"));
        }

        // 3. Application base directory & bundled samples
        string appBase = AppContext.BaseDirectory;
        AddIfValid(appBase);
        AddIfValid(Path.Combine(appBase, "Samples"));
        AddIfValid(Path.Combine(appBase, "image"));
        AddIfValid(Path.Combine(appBase, "images"));
        AddIfValid(Path.Combine(appBase, "imgs"));

        // 4. Sibling directories relative to application executable
        try
        {
            AddIfValid(Path.Combine(appBase, "..", "demo", "image"));
            AddIfValid(Path.Combine(appBase, "..", "demo"));
            AddIfValid(Path.Combine(appBase, "..", "imgs"));
            AddIfValid(Path.Combine(appBase, "..", "images"));
            AddIfValid(Path.Combine(appBase, "..", "Samples"));
            AddIfValid(Path.Combine(appBase, "..", "..", "demo", "image"));
            AddIfValid(Path.Combine(appBase, "..", "..", "imgs"));
            AddIfValid(Path.Combine(appBase, "..", "..", "image"));
            AddIfValid(Path.Combine(appBase, "..", "..", "Samples"));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException or ArgumentException)
        {
        }

        // 5. Current process working directory
        try
        {
            string curDir = Directory.GetCurrentDirectory();
            AddIfValid(curDir);
            AddIfValid(Path.Combine(curDir, "image"));
            AddIfValid(Path.Combine(curDir, "images"));
            AddIfValid(Path.Combine(curDir, "demo", "image"));
            AddIfValid(Path.Combine(curDir, "imgs"));
            AddIfValid(Path.Combine(curDir, "Samples"));
            AddIfValid(Path.Combine(curDir, "..", "demo", "image"));
            AddIfValid(Path.Combine(curDir, "..", "imgs"));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException or ArgumentException)
        {
        }

        // 6. Well-known development and demo directories
        AddIfValid(@"d:\tunvision\mwwz-samples\imgs");
        AddIfValid(@"d:\tunvision\mwwz-samples\demo_mfc_upgraded\image");
        AddIfValid(@"d:\tunvision\mwwz-samples\TunVision\Samples");
        AddIfValid(@"c:\Users\ngocnv\Downloads\demo_mfc_x86_2026.10.22\demo\image");
        AddIfValid(@"c:\Users\ngocnv\Downloads\demo_mfc_x86_2026.10.22\TunVision\Samples");
        AddIfValid(@"c:\Users\ngocnv\Downloads\demo_mfc_x86_2026.11.06\demo\image");
        AddIfValid(@"c:\Users\ngocnv\Downloads\demo_mfc_x86_2026.11.06\TunVision\Samples");

        return dirs;
    }

    /// <summary>
    /// Resolves an image path using multi-tier resolution:
    /// Tier 1: Direct path existence
    /// Tier 2: Candidate directory relative combination
    /// Tier 3: Filename matching across candidate directories
    /// Returns the resolved absolute path, or null if not found.
    /// </summary>
    public static string? ResolveImagePath(string? inputPath, string? projectDir = null, string? currentNodeFolder = null)
    {
        if (string.IsNullOrWhiteSpace(inputPath)) return null;

        string cleanPath = inputPath.Trim('\"', '\'', ' ', '\t', '\r', '\n');
        cleanPath = cleanPath.Replace('/', Path.DirectorySeparatorChar);

        // Tier 1: Direct path existence
        try
        {
            if (File.Exists(cleanPath))
            {
                return Path.GetFullPath(cleanPath);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException or ArgumentException)
        {
        }

        var candidates = GetCandidateImageDirectories(projectDir, currentNodeFolder);

        // Tier 2: Candidate directory relative combination
        foreach (var dir in candidates)
        {
            try
            {
                string combined = Path.Combine(dir, cleanPath);
                if (File.Exists(combined))
                {
                    return Path.GetFullPath(combined);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException or ArgumentException)
            {
            }
        }

        // Tier 3: Filename matching across candidate directories
        string fileName = Path.GetFileName(cleanPath);
        if (!string.IsNullOrEmpty(fileName))
        {
            foreach (var dir in candidates)
            {
                try
                {
                    string combined = Path.Combine(dir, fileName);
                    if (File.Exists(combined))
                    {
                        return Path.GetFullPath(combined);
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException or ArgumentException)
                {
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Checks whether the given path represents an existing and supported image file.
    /// </summary>
    public static bool IsValidImageFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            if (!File.Exists(path)) return false;
            string ext = Path.GetExtension(path);
            return ImageExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
