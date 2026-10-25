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
    /// <summary>
    /// Галочка «завершать принудительно, даже если есть несохранённое» (решение Романа для ShareX и подобных).
    /// Сначала вежливо, через несколько секунд — завершаем, несохранённое в этой программе пропадает.
    /// </summary>
    public bool ForceKill { get; set; }
}

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    /// <summary>Первая настройка пройдена. Пока нет — при запуске открывается «Первая настройка».</summary>
    public bool SetupDone { get; set; }
    /// <summary>Горячая клавиша «Работа ↔ Отдых».</summary>
    public string Hotkey { get; set; } = "Ctrl+Alt+Home";
    /// <summary>Клавиша «обратно в плитки» во время отдыха. Ctrl+Alt+Пробел занят быстрым вызовом Claude.</summary>
    public string TilesHotkey { get; set; } = "Ctrl+Alt+End";
    /// <summary>Показывать окно «Переход в отдых». false — переходить сразу, без вопросов.</summary>
    public bool ConfirmBeforeRest { get; set; } = true;
    /// <summary>Сколько ждать, пока программа закроется (вопросы «Сохранить?» не считаются).</summary>
    public int CloseTimeoutSeconds { get; set; } = 30;
    public List<ProgramSetting> Programs { get; set; } = [];
    /// <summary>Возврат к работе по расписанию — в дополнение к кнопке и горячей клавише.</summary>
    public DaySchedule ReturnSchedule { get; set; } = new();
    /// <summary>Мягкое вечернее предложение отдыха — уведомление у значка в трее.</summary>
    public DaySchedule RestOffer { get; set; } = new() { Time = "19:00" };

    public ProgramSetting? Find(string id) =>
        Programs.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
}
