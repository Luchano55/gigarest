using GigaRest.Core;
using GigaRest.Core.Desktop;
using GigaRest.Core.Input;
using GigaRest.Core.Logging;
using GigaRest.Core.Programs;
using GigaRest.Core.Session;
using GigaRest.Core.Settings;
using GigaRest.Core.Switching;
using GigaRest.Windows;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.XamlTypeInfo;

namespace GigaRest.App;

/// <summary>
/// Программа в трее: значок, горячая клавиша, окно «Работа → Отдых». Состояние «отдых» — это session.json.
/// </summary>
public sealed partial class App : Application, IXamlMetadataProvider
{
    private const int HotkeyToggle = 1;
    private const int MenuToggle = 1, MenuShow = 2, MenuPrograms = 4, MenuLogs = 3, MenuExit = 9;

    private readonly XamlControlsXamlMetaDataProvider _metadata = new();
    private readonly string[] _args;
    private readonly string? _screenshots;
    private readonly SingleInstance? _single;
    private AppPaths _paths = null!;
    private SettingsStore _settingsStore = null!;
    private AppSettings _settings = null!;
    private IDesktop _desktop = null!;
    private RestSwitcher _switcher = null!;
    private TrayAndHotkeys? _tray;
    private SwitcherWindow? _window;
    private ProgramsWindow? _programs;
    private bool _busy;
    private CancellationTokenSource? _cts;
    private string? _hotkeyProblem;

    public static int ExitCode { get; private set; }

    public App(string[] args, string? screenshots, SingleInstance? single)
    {
        _args = args;
        _screenshots = screenshots;
        _single = single;
        UnhandledException += (_, e) =>
        {
            Log.Error("Необработанная ошибка", e.Exception);
            e.Handled = _screenshots is null;
            if (_screenshots is not null) ExitCode = 3;
        };
    }

    // Метаданные стандартных элементов: интерфейс собран в коде, без XAML-разметки.
    public IXamlType GetXamlType(Type type) => _metadata.GetXamlType(type);
    public IXamlType GetXamlType(string fullName) => _metadata.GetXamlType(fullName);
    public XmlnsDefinition[] GetXmlnsDefinitions() => _metadata.GetXmlnsDefinitions();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Resources.MergedDictionaries.Add(new XamlControlsResources());
        RequestedTheme = ApplicationTheme.Light;

        if (_screenshots is not null)
        {
            _ = RunScreenshotsAsync(_screenshots);
            return;
        }

        _paths = AppPaths.Default();
        Log.Init(_paths.LogsDir);
        Log.Info($"GigaRest {typeof(App).Assembly.GetName().Version} запущена");
        _settingsStore = new SettingsStore(_paths.SettingsFile);
        _settings = _settingsStore.Load();
        _desktop = new WindowsDesktop();
        _switcher = new RestSwitcher(_desktop, SystemClock.Instance, new SessionStore(_paths.SessionFile))
        {
            CloseTimeout = TimeSpan.FromSeconds(_settings.CloseTimeoutSeconds),
        };

        var queue = DispatcherQueue.GetForCurrentThread();
        _tray = new TrayAndHotkeys();
        _tray.ShowIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "GigaRest.ico"), TrayTip);
        _tray.TrayClicked += () => queue.TryEnqueue(ShowWindow);
        _tray.HotkeyPressed += id => { if (id == HotkeyToggle) queue.TryEnqueue(Toggle); };
        _tray.MenuItems = MenuItems;
        _tray.MenuCommand += id => queue.TryEnqueue(() => OnMenu(id));
        RegisterHotkey();
        _single?.Listen(queue, ShowWindow);

        if (!_args.Contains("--tray") || _switcher.IsResting)
            ShowWindow();
    }

    private async Task RunScreenshotsAsync(string dir)
    {
        try
        {
            ExitCode = await Screenshots.RunAsync(dir) ? 0 : 2;
        }
        catch (Exception ex)
        {
            ExitCode = 2;
            File.WriteAllText(Path.Combine(dir, "error.txt"), ex.ToString());
        }
        finally
        {
            Exit();
        }
    }

    private string TrayTip => _switcher.IsResting ? "GigaRest — отдых" : "GigaRest — работа";

    private void RegisterHotkey()
    {
        if (!Hotkey.TryParse(_settings.Hotkey, out var hotkey))
        {
            _hotkeyProblem = $"Горячая клавиша «{_settings.Hotkey}» записана с ошибкой — поправьте в settings.json";
        }
        else
        {
            _hotkeyProblem = _tray!.RegisterHotkey(HotkeyToggle, hotkey);
        }
        if (_hotkeyProblem is not null)
        {
            Log.Warn(_hotkeyProblem);
            _tray!.ShowBalloon("Горячая клавиша", _hotkeyProblem);
        }
    }

    private IReadOnlyList<TrayMenuItem?> MenuItems() =>
    [
        new TrayMenuItem(MenuToggle, _switcher.IsResting ? "Вернуться к работе" : "Перейти в отдых…", !_busy, Bold: true),
        new TrayMenuItem(MenuShow, "Показать окно"),
        new TrayMenuItem(MenuPrograms, "Рабочие программы…"),
        null,
        new TrayMenuItem(MenuLogs, "Открыть журнал"),
        null,
        new TrayMenuItem(MenuExit, "Выход"),
    ];

    private void OnMenu(int id)
    {
        switch (id)
        {
            case MenuToggle: Toggle(); break;
            case MenuShow: ShowWindow(); break;
            case MenuPrograms: _ = ShowProgramsAsync(); break;
            case MenuLogs:
                var psi = new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                psi.ArgumentList.Add(_paths.LogsDir);
                System.Diagnostics.Process.Start(psi)?.Dispose();
                break;
            case MenuExit: Quit(); break;
        }
    }

    private SwitcherWindow Window()
    {
        if (_window is null)
        {
            _window = new SwitcherWindow();
            _window.ActionChanged += p =>
            {
                ProgramScanner.Apply(_settings, p);
                SaveSettings();
            };
            _window.GoRequested += () => _ = GoToRestAsync(_window.Programs);
            _window.ReturnRequested += () => _ = ReturnToWorkAsync();
            _window.CancelRequested += () => _cts?.Cancel();
            _window.ProgramsRequested += () => _ = ShowProgramsAsync();
        }
        return _window;
    }

    private void ShowWindow()
    {
        var w = Window();
        if (!_busy)
        {
            if (_switcher.IsResting) w.ShowRestWaiting();
            else w.ShowPlan(Scan(), _desktop.GetMonitors().Count > 1, _hotkeyProblem);
        }
        w.Present();
    }

    /// <summary>Окно «Рабочие программы»: запущенные, настроенные и установленные (ярлыки «Пуск» читаем в фоне).</summary>
    private async Task ShowProgramsAsync()
    {
        if (_programs is null)
        {
            _programs = new ProgramsWindow();
            _programs.Saved += rows =>
            {
                ProgramCatalog.Save(_settings, rows);
                SaveSettings();
                Log.Info($"Рабочие программы: закрывать {rows.Count(r => r.Action == ProgramAction.Close)}, на второй монитор {rows.Count(r => r.Action == ProgramAction.SecondMonitor)}");
                if (!_busy && !_switcher.IsResting && _window is not null)
                    _window.ShowPlan(Scan(), _desktop.GetMonitors().Count > 1, _hotkeyProblem);
            };
        }
        _programs.ShowLoading();
        _programs.Present();
        try
        {
            var running = Scan();
            var shortcuts = await Task.Run(StartMenu.ReadShortcuts);
            _programs.SetRows(ProgramCatalog.BuildRows(_settings, running, shortcuts));
        }
        catch (Exception ex)
        {
            Log.Error("Список программ", ex);
            _programs.SetRows(ProgramCatalog.BuildRows(_settings, Scan(), []));
        }
    }

    private List<RunningProgram> Scan()
    {
        var programs = ProgramScanner.Scan(_desktop.GetProcesses(), _desktop.GetWindows(), _settings, _desktop.CurrentProcessId);
        if (ProgramScanner.RememberNew(_settings, programs)) SaveSettings();
        return programs;
    }

    private void SaveSettings()
    {
        try { _settingsStore.Save(_settings); }
        catch (Exception ex) { Log.Error("Не удалось сохранить настройки", ex); }
    }

    /// <summary>Горячая клавиша и пункт меню: работа → окно перехода (или сразу), отдых → вернуть всё.</summary>
    private void Toggle()
    {
        if (_busy) { _window?.Present(); return; }
        if (_switcher.IsResting) { _ = ReturnToWorkAsync(); return; }
        if (_settings.ConfirmBeforeRest) { ShowWindow(); return; }
        _ = GoToRestAsync(Scan());
    }

    private async Task GoToRestAsync(IReadOnlyList<RunningProgram> programs)
    {
        if (_busy) return;
        _busy = true;
        _cts = new CancellationTokenSource();
        var w = Window();
        try
        {
            w.ShowProgress(programs);
            w.Present();
            var progress = new Progress<SwitchProgress>(w.UpdateProgress);
            var report = await Task.Run(() => _switcher.GoToRestAsync(programs, progress, _cts.Token));
            w.ShowRest(report);
            _tray?.SetTip(TrayTip);
        }
        catch (Exception ex)
        {
            Log.Error("Переход в отдых", ex);
            w.ShowError("Не получилось перейти в отдых: " + ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task ReturnToWorkAsync()
    {
        if (_busy) return;
        _busy = true;
        var w = Window();
        try
        {
            w.ShowReturning();
            w.Present();
            var progress = new Progress<SwitchProgress>(w.UpdateProgress);
            var report = await Task.Run(() => _switcher.ReturnToWork(progress, CancellationToken.None));
            w.ShowWork(report);
            _tray?.SetTip(TrayTip);
            _tray?.ShowBalloon("Работа", report.Summary);
        }
        catch (Exception ex)
        {
            Log.Error("Возврат к работе", ex);
            w.ShowError("Не получилось вернуться к работе: " + ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private void Quit()
    {
        _cts?.Cancel();
        _tray?.Dispose();
        _tray = null;
        _window?.CloseForGood();
        _programs?.CloseForGood();
        Log.Info("Выход");
        Exit();
    }
}
