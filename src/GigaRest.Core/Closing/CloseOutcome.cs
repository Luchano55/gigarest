namespace GigaRest.Core.Closing;

public enum CloseOutcome
{
    /// <summary>Закрылась сама, вежливо.</summary>
    Closed,
    /// <summary>Спрятала окна в трей и была завершена — по галочке «можно завершать».</summary>
    Terminated,
    /// <summary>Окна остались открыты за отведённое время.</summary>
    StillOpen,
    /// <summary>Окна спрятаны, программа работает в фоне; завершать не разрешено.</summary>
    InBackground,
    /// <summary>Запущена от администратора — закрыть может только пользователь.</summary>
    Elevated,
    /// <summary>Переход отменён.</summary>
    Cancelled,
}

public enum CloseStage
{
    Waiting,
    Closing,
    /// <summary>Программа спрашивает («Сохранить?») — ждём пользователя, таймер стоит.</summary>
    Asking,
    Done,
}

public sealed record CloseProgress(string ProgramId, string DisplayName, CloseStage Stage, CloseOutcome? Outcome);

public sealed record CloseResult(string ProgramId, string DisplayName, CloseOutcome Outcome, bool WasAsked)
{
    /// <summary>Программа ушла — её запускаем обратно при возврате.</summary>
    public bool IsGone => Outcome is CloseOutcome.Closed or CloseOutcome.Terminated;

    public string Message => Outcome switch
    {
        CloseOutcome.Closed => $"{DisplayName} — закрыта",
        CloseOutcome.Terminated => $"{DisplayName} — завершена (окна были спрятаны в трей)",
        CloseOutcome.StillOpen => $"{DisplayName} не закрылась — закройте сами или оставьте",
        CloseOutcome.InBackground => $"{DisplayName} спряталась в трей и работает в фоне. Чтобы закрывать её полностью, поставьте галочку «можно завершать»",
        CloseOutcome.Elevated => $"{DisplayName} запущена от администратора — её закроете только вы",
        CloseOutcome.Cancelled => $"{DisplayName} — переход отменён",
        _ => DisplayName,
    };
}
