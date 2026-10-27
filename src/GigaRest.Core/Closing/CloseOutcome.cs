using GigaRest.Core.Text;

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
        CloseOutcome.Closed => L.T($"{DisplayName} — закрыта", $"{DisplayName} — closed"),
        CloseOutcome.Terminated => L.T($"{DisplayName} — завершена (окна были спрятаны в трей)", $"{DisplayName} — ended (its windows were hidden in the tray)"),
        CloseOutcome.StillOpen => L.T($"{DisplayName} не закрылась — закройте сами или оставьте", $"{DisplayName} didn't close — close it yourself or leave it open"),
        CloseOutcome.InBackground => L.T($"{DisplayName} спряталась в трей и работает в фоне. Чтобы закрывать её полностью, поставьте галочку «можно завершать»", $"{DisplayName} hid in the tray and keeps running in the background. To close it fully, tick “OK to end”"),
        CloseOutcome.Elevated => L.T($"{DisplayName} запущена от администратора — её закроете только вы", $"{DisplayName} runs as administrator — only you can close it"),
        CloseOutcome.Cancelled => L.T($"{DisplayName} — переход отменён", $"{DisplayName} — switch cancelled"),
        _ => DisplayName,
    };
}
