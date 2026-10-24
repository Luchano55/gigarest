using System.Text;

namespace GigaRest.Core.Desktop;

/// <summary>
/// Разбор командной строки Windows на аргументы — по тем же правилам, что CommandLineToArgvW
/// и рантайм C. Нужен, чтобы запустить программу обратно через ArgumentList, без склейки строк.
/// </summary>
public static class CommandLine
{
    public static List<string> Split(string? commandLine)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(commandLine))
            return result;

        var i = 0;
        var s = commandLine;

        // Первый аргумент (имя программы): кавычки только группируют, обратная косая не особая.
        var first = new StringBuilder();
        var inQuotes = false;
        for (; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (!inQuotes && (c == ' ' || c == '\t')) break;
            first.Append(c);
        }
        result.Add(first.ToString());

        while (true)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t')) i++;
            if (i >= s.Length) break;

            var arg = new StringBuilder();
            inQuotes = false;
            for (; i < s.Length; i++)
            {
                var c = s[i];
                if (c == '\\')
                {
                    var slashes = 0;
                    while (i < s.Length && s[i] == '\\') { slashes++; i++; }
                    if (i < s.Length && s[i] == '"')
                    {
                        arg.Append('\\', slashes / 2);
                        if (slashes % 2 == 1) { arg.Append('"'); continue; }
                        i--; // кавычку обработает следующий шаг
                        continue;
                    }
                    arg.Append('\\', slashes);
                    i--;
                    continue;
                }
                if (c == '"')
                {
                    // "" внутри кавычек — буквальная кавычка (правило рантайма C с 2008 года).
                    if (inQuotes && i + 1 < s.Length && s[i + 1] == '"') { arg.Append('"'); i++; continue; }
                    inQuotes = !inQuotes;
                    continue;
                }
                if (!inQuotes && (c == ' ' || c == '\t')) break;
                arg.Append(c);
            }
            result.Add(arg.ToString());
        }
        return result;
    }
}
