using GigaRest.Core.Desktop;
using GigaRest.Core.Logging;

namespace GigaRest.Core.Session;

/// <summary>
/// Что сделано при переходе в отдых — чтобы вернуть как было. Лежит в session.json
/// и переживает перезагрузку.
/// </summary>
public sealed class RestSession
{
    public int Version { get; set; } = 1;
    public DateTime StartedUtc { get; set; }
    /// <summary>Закрытые программы — их запускаем обратно.</summary>
    public List<ClosedProgram> Closed { get; set; } = [];
    /// <summary>Окна, перенесённые на второй монитор, — их ставим на место.</summary>
    public List<MovedWindow> Moved { get; set; } = [];
}

public sealed class ClosedProgram
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public List<LaunchSpec> Launch { get; set; } = [];
    /// <summary>Где стояли окна (по одному на окно). Если окно одно — после запуска ставим его так же.</summary>
    public List<WindowPlacementData> Placements { get; set; } = [];
    /// <summary>false, пока программа закрывается; true — закрылась, при возврате запускаем.</summary>
    public bool Gone { get; set; }
}

public sealed class MovedWindow
{
    public string ProgramId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public long Handle { get; set; }
    public int Pid { get; set; }
    public DateTime ProcessStartUtc { get; set; }
    public WindowPlacementData Original { get; set; } = new();
}

public sealed class SessionStore
{
    private readonly string _path;

    public SessionStore(string path)
    {
        _path = path;
    }

    public bool Exists => File.Exists(_path);

    public RestSession? Load() => JsonFiles.Read<RestSession>(_path, Log.Warn);

    public void Save(RestSession session) => JsonFiles.Write(_path, session);

    /// <summary>Отдых закончен — файл больше не нужен (копию оставляем для разбора).</summary>
    public void Clear()
    {
        if (!File.Exists(_path)) return;
        File.Move(_path, _path + ".last", overwrite: true);
    }
}
