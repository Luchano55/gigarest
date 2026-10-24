using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using GigaRest.Core.Logging;
using GigaRest.Core.Programs;

namespace GigaRest.Windows;

/// <summary>Установленные программы — по ярлыкам меню «Пуск» (своего и общего). Только чтение.</summary>
public static class StartMenu
{
    public static List<ShortcutInfo> ReadShortcuts()
    {
        var result = new List<ShortcutInfo>();
        string[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
        ];
        foreach (var root in roots.Where(Directory.Exists))
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.lnk", new EnumerationOptions
                {
                    RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 4,
                }).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var file in files)
            {
                var target = Resolve(file);
                if (!string.IsNullOrEmpty(target))
                    result.Add(new ShortcutInfo(Path.GetFileNameWithoutExtension(file), target));
            }
        }
        return result;
    }

    /// <summary>Куда ведёт ярлык. null — не удалось прочитать.</summary>
    public static string? Resolve(string lnkPath)
    {
        object? link = null;
        try
        {
            link = new ShellLink();
            ((IPersistFile)link).Load(lnkPath, 0);
            var sb = new StringBuilder(1024);
            ((IShellLinkW)link).GetPath(sb, sb.Capacity, 0, 0);
            var path = sb.ToString();
            return path.Length > 0 ? Environment.ExpandEnvironmentVariables(path) : null;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException or UnauthorizedAccessException)
        {
            Log.Warn($"Ярлык не прочитан: {Path.GetFileName(lnkPath)} ({ex.GetType().Name})");
            return null;
        }
        finally
        {
            if (link is not null) Marshal.FinalReleaseComObject(link);
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    /// <summary>Нужен только первый метод — GetPath; порядок в таблице методов совпадает с IShellLinkW.</summary>
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int max, nint findData, uint flags);
    }
}
