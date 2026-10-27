namespace GigaRest.Core.Desktop;

/// <summary>
/// Дерево процессов. Родитель считается родителем, только если стартовал раньше ребёнка:
/// иначе это чужой процесс с переиспользованным PID.
/// </summary>
public sealed class ProcessTree
{
    private readonly Dictionary<int, ProcessInfo> _byPid;
    private readonly Dictionary<int, List<ProcessInfo>> _children = new();

    public ProcessTree(IEnumerable<ProcessInfo> processes)
    {
        _byPid = new Dictionary<int, ProcessInfo>();
        foreach (var p in processes)
            _byPid[p.Pid] = p;
        foreach (var p in _byPid.Values)
        {
            var parent = Parent(p);
            if (parent is null) continue;
            if (!_children.TryGetValue(parent.Pid, out var list))
                _children[parent.Pid] = list = [];
            list.Add(p);
        }
    }

    public ProcessInfo? Get(int pid) => _byPid.GetValueOrDefault(pid);

    public ProcessInfo? Parent(ProcessInfo p)
    {
        if (p.ParentPid == p.Pid || !_byPid.TryGetValue(p.ParentPid, out var parent))
            return null;
        return parent.StartTimeUtc <= p.StartTimeUtc ? parent : null;
    }

    /// <summary>Самый верхний предок с тем же exe — «корень» программы (у Chrome — процесс без --type=).</summary>
    public ProcessInfo RootOf(ProcessInfo p)
    {
        var current = p;
        var guard = 0;
        while (guard++ < 64)
        {
            var parent = Parent(current);
            if (parent is null || !string.Equals(parent.ExeName, current.ExeName, StringComparison.OrdinalIgnoreCase))
                break;
            current = parent;
        }
        return current;
    }

    /// <summary>Процесс и все его потомки.</summary>
    public IEnumerable<ProcessInfo> Descendants(ProcessInfo root)
    {
        var seen = new HashSet<int>();
        var stack = new Stack<ProcessInfo>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var p = stack.Pop();
            if (!seen.Add(p.Pid)) continue;
            yield return p;
            if (_children.TryGetValue(p.Pid, out var kids))
                foreach (var k in kids) stack.Push(k);
        }
    }
}
