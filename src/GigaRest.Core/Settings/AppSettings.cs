namespace GigaRest.Core.Settings;

/// <summary>Что делать с программой при переходе в отдых.</summary>
public enum ProgramAction
{
    /// <summary>Не трогать.</summary>
    Keep,
    /// <summary>Вежливо закрыть, при возврате запустить снова.</summary>
    Close,
    /// <summary>Перенести на второй монитор (позже — живая плитка).</summary>
    SecondMonitor,
}

public sealed class ProgramSetting
{
    /// <summary>Ключ программы: имя exe в нижнем регистре или AUMID.</summary>
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public ProgramAction Action { get; set; } = ProgramAction.Keep;
    /// <summary>
    /// Галочка «можно завершать»: если программа спрятала окно в трей и ни о чём не спрашивает,
    /// её можно завершить. По умолчанию выключена.
    /// </summary>
    public bool AllowTerminate { get; set; }
}

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    /// <summary>Горячая клавиша «Работа ↔ Отдых».</summary>
    public string Hotkey { get; set; } = "Ctrl+Alt+Home";
    /// <summary>Показывать окно «Переход в отдых». false — переходить сразу, без вопросов.</summary>
    public bool ConfirmBeforeRest { get; set; } = true;
    /// <summary>Сколько ждать, пока программа закроется (вопросы «Сохранить?» не считаются).</summary>
    public int CloseTimeoutSeconds { get; set; } = 30;
    public List<ProgramSetting> Programs { get; set; } = [];

    public ProgramSetting? Find(string id) =>
        Programs.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
}
