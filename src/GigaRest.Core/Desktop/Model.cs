namespace GigaRest.Core.Desktop;

/// <summary>Процесс. Пара (Pid, StartTimeUtc) однозначна: PID в Windows переиспользуются.</summary>
public sealed record ProcessInfo
{
    public required int Pid { get; init; }
    public int ParentPid { get; init; }
    public DateTime StartTimeUtc { get; init; }
    /// <summary>Имя exe, например «chrome.exe».</summary>
    public required string ExeName { get; init; }
    /// <summary>Полный путь к exe; null, если Windows не дала прочитать.</summary>
    public string? ExePath { get; init; }
    public string? CommandLine { get; init; }
    /// <summary>Личная память процесса (private bytes).</summary>
    public long PrivateBytes { get; init; }
    /// <summary>AUMID для программ из Store и других упакованных, иначе null.</summary>
    public string? Aumid { get; init; }
    /// <summary>Запущен от администратора (или недоступен нам по той же причине).</summary>
    public bool IsElevated { get; init; }

    public ProcessKey Key => new(Pid, StartTimeUtc);
}

public readonly record struct ProcessKey(int Pid, DateTime StartTimeUtc);

public readonly record struct Rect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

/// <summary>Окно верхнего уровня (или принадлежащее другому окну).</summary>
public sealed record WindowInfo
{
    public required long Handle { get; init; }
    /// <summary>Процесс программы. Для Store-программ — процесс самой программы, а не ApplicationFrameHost.</summary>
    public required int Pid { get; init; }
    public string ClassName { get; init; } = "";
    /// <summary>Заголовок. Только для показа на экране, в журнал не пишется.</summary>
    public string Title { get; init; } = "";
    public bool IsVisible { get; init; }
    public bool IsEnabled { get; init; } = true;
    public long OwnerHandle { get; init; }
    public bool IsToolWindow { get; init; }
    /// <summary>Скрыто системой (окно на другом виртуальном столе, «спящая» Store-программа).</summary>
    public bool IsCloaked { get; init; }
    public bool IsMinimized { get; init; }
    public Rect Bounds { get; init; }

    /// <summary>Окно, которое видно в Alt+Tab: видимое, без владельца, не служебное.</summary>
    public bool IsAppWindow =>
        IsVisible && OwnerHandle == 0 && !IsToolWindow && !IsCloaked && Title.Length > 0;

    /// <summary>Похоже на вопрос программы («Сохранить?»): видимый диалог с владельцем.</summary>
    public bool IsDialog => IsVisible && !IsCloaked && OwnerHandle != 0 && ClassName == "#32770";
}

public sealed record MonitorInfo
{
    public required string Id { get; init; }
    public bool IsPrimary { get; init; }
    public Rect Bounds { get; init; }
    public Rect WorkArea { get; init; }
    /// <summary>Масштаб в процентах (100, 125, 150…).</summary>
    public int ScalePercent { get; init; } = 100;
}

/// <summary>Положение окна для возврата: где было и в каком виде.</summary>
public sealed record WindowPlacementData
{
    public Rect NormalBounds { get; init; }
    public bool Maximized { get; init; }
    public bool Minimized { get; init; }
    public string? MonitorId { get; init; }
}

/// <summary>Как запустить программу обратно.</summary>
public sealed record LaunchSpec
{
    /// <summary>Путь к exe. Пусто, если запуск по AUMID.</summary>
    public string FileName { get; init; } = "";
    /// <summary>Аргументы по одному — передаются через ArgumentList, без склейки.</summary>
    public List<string> Arguments { get; init; } = [];
    public string? WorkingDirectory { get; init; }
    /// <summary>Запуск Store-программы: explorer.exe shell:AppsFolder\&lt;AUMID&gt;.</summary>
    public string? Aumid { get; init; }
}
