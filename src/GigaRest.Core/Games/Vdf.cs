namespace GigaRest.Core.Games;

/// <summary>
/// Текстовый формат Steam (VDF/ACF): «"ключ" "значение"» и вложенные «"ключ" { … }».
/// Регистр ключей не важен. Повреждённый файл не роняет разбор — читаем, сколько получилось.
/// </summary>
public sealed class VdfNode
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, VdfNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? this[string key] => Values.TryGetValue(key, out var v) ? v : null;
    public VdfNode? Child(string key) => Children.TryGetValue(key, out var c) ? c : null;

    public static VdfNode Parse(string text)
    {
        var pos = 0;
        var root = new VdfNode();
        ParseInto(root, text, ref pos);
        return root;
    }

    private static void ParseInto(VdfNode node, string text, ref int pos)
    {
        while (true)
        {
            var key = Next(text, ref pos);
            if (key is null || key == "}") return;
            if (key == "{") continue;
            var value = Next(text, ref pos);
            if (value is null) return;
            if (value == "{")
            {
                var child = new VdfNode();
                ParseInto(child, text, ref pos);
                node.Children[key] = child;
            }
            else if (value != "}")
            {
                node.Values[key] = value;
            }
            else return;
        }
    }

    // Следующий токен: строка в кавычках (с \\ и \"), «{», «}». Комментарии «//» пропускаем.
    private static string? Next(string text, ref int pos)
    {
        while (pos < text.Length)
        {
            var c = text[pos];
            if (char.IsWhiteSpace(c)) { pos++; continue; }
            if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '/')
            {
                while (pos < text.Length && text[pos] != '\n') pos++;
                continue;
            }
            if (c is '{' or '}') { pos++; return c.ToString(); }
            if (c == '"')
            {
                pos++;
                var sb = new System.Text.StringBuilder();
                while (pos < text.Length && text[pos] != '"')
                {
                    if (text[pos] == '\\' && pos + 1 < text.Length) { pos++; sb.Append(text[pos] switch { 'n' => '\n', 't' => '\t', var x => x }); }
                    else sb.Append(text[pos]);
                    pos++;
                }
                pos++;
                return sb.ToString();
            }
            // Слово без кавычек — до пробела.
            var start = pos;
            while (pos < text.Length && !char.IsWhiteSpace(text[pos]) && text[pos] is not ('{' or '}' or '"')) pos++;
            return text[start..pos];
        }
        return null;
    }
}
