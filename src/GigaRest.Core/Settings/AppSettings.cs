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
    /// <summary>Последняя выбранная сцена «Тихого» экрана и эффект поверх (null — эффект сцены по умолчанию).</summary>
    public string? SceneId { get; set; }
    public Scenes.SceneEffect? SceneEffect { get; set; }
    /// <summary>Папка «Мои сцены»; пусто — «Изображения\GigaRest».</summary>
    public string? MyScenesFolder { get; set; }
    /// <summary>Когда игру запускали из «Играть» (код игры → время, UTC): Epic и GOG сами этого не хранят.</summary>
    public Dictionary<string, DateTime> GamesPlayed { get; set; } = [];
    /// <summary>Папка с фильмами для «Смотреть»; пусто — «Видео» пользователя.</summary>
    public string? FilmsFolder { get; set; }
    /// <summary>Какие фильмы открывали из «Смотреть» (путь → время, UTC) — для «Продолжить».</summary>
    public Dictionary<string, DateTime> FilmsOpened { get; set; } = [];
    /// <summary>Где остановились во встроенном плеере (путь → секунды) — «Продолжить» с этого места.</summary>
    public Dictionary<string, double> FilmsPosition { get; set; } = [];
    /// <summary>Плеер для фильмов: пусто — встроенный (фильм открывается прямо на «Тихом» экране), иначе путь к exe.</summary>
    public string? FilmPlayer { get; set; }
    /// <summary>Музыкальная программа для «Слушать»: пусто — найти самим (Яндекс Музыка и другие из меню «Пуск»).</summary>
    public string? MusicApp { get; set; }
    /// <summary>Откуда брать обновления: auto (как при установке), stable, dev.</summary>
    public string UpdateChannel { get; set; } = Updates.UpdateChannels.Auto;
    /// <summary>Один раз перевели Яндекс Музыку со второго монитора в «Оставить» (ей управляет «Слушать», просьба Романа).</summary>
    public bool MusicOnMainScreen { get; set; }
    /// <summary>На время отдыха включать «Не беспокоить» (только приоритетные уведомления).</summary>
    public bool DoNotDisturb { get; set; } = true;
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
