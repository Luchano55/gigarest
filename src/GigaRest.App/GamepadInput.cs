using Microsoft.UI.Dispatching;
using Windows.Gaming.Input;

namespace GigaRest.App;

/// <summary>Кнопка геймпада для «Тихого» экрана.</summary>
public enum PadButton { A, B, Y, LeftShoulder, RightShoulder, Up, Down, Left, Right }

/// <summary>
/// Геймпад на «Тихом» экране (ТЗ, раздел 5): опрашиваем первый подключённый геймпад 20 раз в секунду
/// и сообщаем о нажатиях (не об удержании). Работает, только пока активно окно «Тихого» экрана —
/// во время игры геймпад её. Таймер останавливается в Dispose.
/// </summary>
public sealed class GamepadInput : IDisposable
{
    private readonly DispatcherQueueTimer _timer;
    private readonly Func<bool> _active;
    private GamepadButtons _last;

    public event Action<PadButton>? Pressed;

    public GamepadInput(DispatcherQueue queue, Func<bool> active)
    {
        _active = active;
        _timer = queue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(50);
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
    }

    private void Poll()
    {
        Gamepad? pad;
        try { pad = Gamepad.Gamepads.FirstOrDefault(); }
        catch (Exception) { return; } // нет службы ввода — просто без геймпада
        if (pad is null) { _last = GamepadButtons.None; return; }
        var buttons = pad.GetCurrentReading().Buttons;
        var down = buttons & ~_last; // только что нажатые
        _last = buttons;
        if (down == GamepadButtons.None || !_active()) return;
        foreach (var (flag, button) in Map)
            if ((down & flag) != 0) Pressed?.Invoke(button);
    }

    private static readonly (GamepadButtons, PadButton)[] Map =
    [
        (GamepadButtons.A, PadButton.A), (GamepadButtons.B, PadButton.B), (GamepadButtons.Y, PadButton.Y),
        (GamepadButtons.LeftShoulder, PadButton.LeftShoulder), (GamepadButtons.RightShoulder, PadButton.RightShoulder),
        (GamepadButtons.DPadUp, PadButton.Up), (GamepadButtons.DPadDown, PadButton.Down),
        (GamepadButtons.DPadLeft, PadButton.Left), (GamepadButtons.DPadRight, PadButton.Right),
    ];

    public void Dispose() => _timer.Stop();
}
