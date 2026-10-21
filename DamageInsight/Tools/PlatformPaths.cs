using System;
using System.IO;
using System.Linq;

namespace DamageInsight.Tools;

/// <summary>
/// What differs between the Windows version of the game (Windows itself, and Linux / Steam Deck through Proton) and
/// the native Mac version (docs/MACOS.md). Plain logic, so the tests cover both sides on a Windows PC.
/// </summary>
public static class PlatformPaths
{
    /// <summary>True in the Windows version of the game (also under Wine/Proton), false in the Mac version.</summary>
    public static bool IsWindows => Path.DirectorySeparatorChar == '\\';

    /// <summary>
    /// The file:// address that Application.OpenURL opens a folder with. Windows keeps the form it always had
    /// ("file:///C:/..."); on a Mac every part is escaped, because Steam's folder there is in "Application Support".
    /// </summary>
    public static string FolderUrl(string folder, bool windows) =>
        windows
            ? "file:///" + folder.Replace('\\', '/')
            : "file://" + string.Join("/", folder.Split('/').Select(Uri.EscapeDataString));

    /// <summary>Folders to look in for a font file by name (macOS: the system's, then the ones the player added).</summary>
    public static string[] FontFolders(bool windows) =>
        windows
            ? new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts") }
            : new[]
            {
                "/System/Library/Fonts", "/System/Library/Fonts/Supplemental", "/Library/Fonts",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "Fonts"),
            };
}
