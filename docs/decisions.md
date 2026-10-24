# Принятые решения

Проверено исследованием 24.10.2026. Пункты с (?) — не подтверждены источниками, проверить прототипом на ПК Романа.

## D-UI. Интерфейс — WinUI 3 + Win2D, не WPF
- **Почему не WPF.** У WPF известные рывки на 144 Гц и VRR. Задача dotnet/wpf#2294 открыта, в 2024 году мейнтейнер ответил «Sorry no». Фактический потолок около 60 кадров/с, `DesiredFrameRate` частоту только понижает. `MediaElement` плохо справляется с 4K HEVC и с петлёй.
- **Почему WinUI 3.** Анимации Composition (Ken Burns, прозрачность, размытие) выполняет системный композитор с частотой монитора. Частицы — Win2D `CanvasAnimatedControl` / `CanvasSpriteBatch`.
- **Запасной вариант** — сцена в WebView2 (HTML/canvas): плавно и быстро в разработке, но тяжелее по памяти.
- **Следствие:** приложение собирается только на Windows (в CI). Логика — в `GigaRest.Core` (net8.0), она тестируется на Linux.
- Источники: https://github.com/dotnet/wpf/issues/2294

## D-FPS. Частота кадров
- Движение считать от реального времени кадра (delta-time), а не по счётчику кадров.
- Спокойная сцена по умолчанию ограничена ~82 кадрами/с, настраивается.
- Цель — нагрузка на видеокарту в спокойной сцене не выше 10%.

## D-VIDEO. Видео-сцены
- Видео играет WinRT `MediaPlayer` в режиме frame server (`IsVideoFrameServerEnabled`, `CopyFrameToVideoSurface` → Win2D). Декодирование аппаратное.
- Для HEVC нужно расширение «HEVC Video Extensions».
- Бесшовность `IsLoopingEnabled` не документирована (?). Если на стыке будет шов — два плеера с перекрёстным переходом. Ещё один запасной вариант — libmpv.
- LibVLCSharp не брать: чёрные вспышки на 4K HEVC (LibVLCSharp#421).
- Совет пользователю: ролики сцен без звука, с ключевым кадром в начале. Перекодировать можно через DropWheel или ffmpeg.

## D-PAUSE. Пауза, когда идёт игра
- Раз в 1–2 с опрашивать `SHQueryUserNotificationState`: значения `QUNS_RUNNING_D3D_FULL_SCREEN`, `QUNS_BUSY`, `QUNS_PRESENTATION_MODE` означают паузу.
- Дополнительно проверять, что активное окно не наше и закрывает весь монитор (окно без рамки), как в Chromium `fullscreen_win.cc`.
- Если игру запустили из GigaRest — пауза сразу.
- На паузе останавливать рендер и освобождать декодер.

## D-THUMBS. Живые плитки — DWM Thumbnails
- **API:** `DwmRegisterThumbnail(наше окно, окно программы)` → `DwmUpdateThumbnailProperties` → `DwmUnregisterThumbnail`. Картинку собирает сам DWM на видеокарте, наш процесс почти не нагружается.
- **Миниатюра рисуется поверх содержимого нашего окна.** Подписи и бейджи — рядом с прямоугольником миниатюры, не поверх.
- **Свёрнутое окно живьём не показывается** — DWM держит последний кадр. Поэтому окна плиток «паркуются» развёрнутыми под экраном плиток.
- **Chromium и Electron** (Chrome, Claude) перестают рисовать полностью перекрытое окно (native window occlusion). Тогда плитка показывает последний кадр с подписью.
  - Для своего Chrome можно передать флаг `--disable-features=CalculateNativeWinOcclusion` (?).
- **Порядок:** этап 4 начинается с прототипа.
- Источники: https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmregisterthumbnail , https://chromium.googlesource.com/chromium/src/+/master/docs/windows_native_window_occlusion_tracking.md

## D-WINDOWS. Перенос окон
- **Сначала сохранить положение:** `GetWindowPlacement` и `GetWindowRect`.
- **Перенос:** `ShowWindow(SW_RESTORE)` → `SetWindowPos` в рабочую область второго монитора → `ShowWindow(SW_MAXIMIZE)`. Разворачивать **после** переноса: окно само подстроится под DPI монитора.
- Приложение — PerMonitorV2.
- Сворачивание и закрытие чужих окон ловить через `SetWinEventHook` (`EVENT_SYSTEM_MINIMIZESTART/END`, `EVENT_OBJECT_DESTROY`) с флагами `WINEVENT_OUTOFCONTEXT | SKIPOWNPROCESS`. Делегат держать в поле, работу передавать в UI-поток.
- **Программы из Store:** окно принадлежит `ApplicationFrameHost.exe`. Окна с правами администратора недоступны (UIPI) — предупреждать.
- Предупреждение Рэймонда Чена: управлять чужими окнами рискованно. Поэтому всё делаем с проверкой результата. https://devblogs.microsoft.com/oldnewthing/20210118-00/?p=104725

## D-CLOSE. Вежливое закрытие
- **Закрытие:** `EnumWindows` → все видимые окна процесса верхнего уровня без владельца → `PostMessage(WM_CLOSE)`.
- **Вопрос «Сохранить?»:** признак — главное окно отключено, есть всплывающее окно `#32770` / `GW_ENABLEDPOPUP`. Тогда статус «программа спрашивает», окно — вперёд, ждём.
- **Командная строка для перезапуска** — `NtQueryInformationProcess` или WMI `Win32_Process.CommandLine`. У Chrome и Electron брать корневой процесс без `--type=`.
- **Дерево процессов** — `CreateToolhelp32Snapshot`; учитывать время старта, чтобы не перепутать переиспользованный PID.
- **Рабочий Chrome — отдельная папка данных** (`--user-data-dir`). Закрыть только один профиль внутри общего процесса надёжно нельзя.

## D-DND. «Не беспокоить» — по возможности, с проверкой
- **Публичного API нет.** `FocusSessionManager` — функция с ограниченным доступом (нужен токен Microsoft).
- **Стратегии по порядку:**
  1. настройки уведомлений в HKCU (CloudStore quiethours или `NOC_GLOBAL_SETTING_TOASTS_ENABLED`) с перезапуском `WpnUserService_*` — проверить на 24H2 (?);
  2. открыть `ms-settings:notifications` с подсказкой.
- Результат всегда проверять и возвращать прежнее состояние.
- Источники: https://learn.microsoft.com/en-us/uwp/api/windows.ui.shell.focussessionmanager.trystartfocussession , https://learn.microsoft.com/en-us/answers/a/12428433 , https://www.elevenforum.com/t/help-to-find-registry-key-that-turns-off-notification-on-w11-24h2.25353/

## D-AUDIO. Устройство вывода
- `IPolicyConfig::SetDefaultEndpoint` (недокументированный COM, им пользуются EarTrumpet и SoundSwitch) для ролей eConsole и eMultimedia.
- Перечислять устройства — через NAudio `MMDeviceEnumerator`.
- Хранить ID устройства.

## D-PAD. Геймпад
- **XInput** (`XInputGetState`, опрос ~60 Гц) работает без фокуса окна. Через него — включение отдыха долгим View+Menu.
- **Windows.Gaming.Input** — по желанию, внутри сцены: он получает ввод только при фокусе.
- Кнопку Xbox (Guide) не используем: её забирает Game Bar.

## D-GAMES. Библиотеки игр
- **Сначала оценить** готовую библиотеку GameLib.NET (Steam, Epic, GOG, Battle.net, Ubisoft).
- **Steam:** `libraryfolders.vdf`, `appmanifest_*.acf`, время последней игры — в `localconfig.vdf`. Запуск — `steam://rungameid/<id>`.
- **Epic:** `ProgramData\Epic\EpicGamesLauncher\Data\Manifests\*.item` (JSON). Запуск — `com.epicgames.launcher://apps/<AppName>?action=launch&silent=true`.
- **Battle.net:** `ProgramData\Battle.net\Agent\product.db` (protobuf). Запуск — `Battle.net.exe --exec="launch <код>"`, коды проверять (?).
- **GOG:** `galaxy-2.0.db` (SQLite). Таблицы проверять при запуске (?).
- **«Последняя игра»** — свой журнал запусков плюс Steam.

## D-OLED. Защита OLED
- **Сдвиги:** статичные элементы смещаются на ±10–30 px каждые 1–3 мин.
- **Яркость:** текст 85–90% яркости. Через 15 мин без ввода — затемнение до 10%, через 60 мин — предложить выключить экран (`SC_MONITORPOWER`).
- **Курсор и панель задач** прячутся.
- **Не мешать уходу экрана в сон:** `ES_DISPLAY_REQUIRED` бесконечно не держать.
- Pixel refresh монитора не мешать.
- Источник: https://us.msi.com/blog/how-msi-oled-care-prevents-oled-from-burn-in

## D-SYS. Права и запуск
- **Без администратора:** UIPI, перетаскивание, другой контекст.
- **Автозапуск** — `HKCU\…\Run`.
- **Горячие клавиши** — `RegisterHotKey`, всегда проверять результат.
- **Win+Esc занята** («выключить Экранную лупу») — не используем. https://support.microsoft.com/en-us/help/13810
- **Схема питания** — `PowerSetActiveScheme`. Игровой режим Windows программно не включается — из ТЗ убран.
