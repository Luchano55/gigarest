<h1 align="center">GigaRest</h1>

<p align="center">
  <b>English</b> · <a href="README.ru.md">Русский</a>
</p>

<p align="center">
  <b>Rest mode for Windows 10/11.</b> One button turns your work PC into a home PC.<br>
  Work apps close politely, notifications go quiet, a calm scene fills your main monitor,<br>
  and live tiles of the windows you want stay on the second one. Another button brings everything back.
</p>

<p align="center">
  <a href="https://github.com/Luchano55/gigarest/actions/workflows/build.yml"><img src="https://img.shields.io/github/actions/workflow/status/Luchano55/gigarest/build.yml?label=build&logo=githubactions&logoColor=white" alt="Build status"></a>
  <a href="https://github.com/Luchano55/gigarest/releases/latest"><img src="https://img.shields.io/github/v/release/Luchano55/gigarest?label=version&color=0078D4" alt="Latest version"></a>
  <a href="https://github.com/Luchano55/gigarest/releases/tag/dev-build"><img src="https://img.shields.io/github/v/release/Luchano55/gigarest?include_prereleases&sort=date&display_name=release&label=test%20build&color=6e7781" alt="Latest test build"></a>
  <a href="#install"><img src="https://img.shields.io/badge/Windows-10%20%7C%2011%20(x64)-0078D4" alt="Windows 10 | 11"></a>
  <a href="https://dotnet.microsoft.com/download/dotnet/8.0"><img src="https://img.shields.io/badge/.NET-8-512BD4?logo=dotnet&logoColor=white" alt=".NET 8"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-green" alt="MIT license"></a>
  <a href="#privacy"><img src="https://img.shields.io/badge/100%25-offline-2ea44f" alt="Offline"></a>
  <a href="#privacy"><img src="https://img.shields.io/badge/no-admin%20rights-2ea44f" alt="No admin rights"></a>
</p>

<p align="center">
  <a href="https://github.com/Luchano55/gigarest/releases/latest/download/GigaRest-Setup.exe"><img src="https://img.shields.io/github/v/release/Luchano55/gigarest?label=Download%20for%20Windows&style=for-the-badge&color=0078D4" alt="Download GigaRest for Windows"></a>
</p>

<p align="center">
  <a href="#how-it-works">How it works</a> ·
  <a href="#features">Features</a> ·
  <a href="#screenshots">Screenshots</a> ·
  <a href="#install">Install</a> ·
  <a href="#faq">FAQ</a> ·
  <a href="#whats-next">What's next</a>
</p>

> The app speaks English and Russian. It follows your Windows language; you can switch it in Settings → Language. The screenshots below show the Russian interface, except the one marked English.

## How it works

1. In the evening, press **Ctrl+Alt+Home** (or the tray icon → "Work → Rest").
2. GigaRest politely closes your work apps — only the ones you marked. If an app asks "Save changes?", it waits for your answer.
3. Your main monitor shows the Quiet screen: a landscape or your own wallpaper with gentle snow, fog, fireflies or rain. At the bottom, one line: **Continue · Play · Watch · Listen · Scenes**.
4. Your second monitor shows up to four live tiles: a messenger, your personal browser, music. Click one to open the window; "Minimize" turns it back into a tile.
5. In the morning, "Back to work": what was closed opens again, and windows return to their places.

## Features

- **Polite closing.** Nothing is ended by force unless you ticked the box. Apps that hide in the tray and background apps are handled the way you choose and later start again with the same parameters. You can also close an app for good — then it won't reopen after rest.
- **Second monitor as a picture.** Drag tiles right in the main window to choose which windows become tiles and in what order.
- **Memory.** The summary shows how much was freed.
- **Do not disturb.** Only priority notifications while you rest, then back to normal.
- **Quiet screen.** Built-in landscapes and a "My scenes" folder for your own 4K pictures and videos; slow motion and effects on top. Kind to OLED: nothing stays still, the cursor hides, the screen dims when idle and turns off after an hour. Mouse and keyboard control. While a fullscreen game runs, the scene pauses and leaves the GPU alone.
- **Play.** One library of installed games from Steam, Epic, GOG and Battle.net plus your own games outside stores — with covers (your own too), search and store filters; one click to launch, without the launcher window.
- **Watch.** YouTube right on the Quiet screen — the home page or the last video you watched; your movies folder and a built-in player that resumes where you stopped; or your own player.
- **Listen.** Yandex Music right on the Quiet screen; nature sounds (rain, sea, campfire, wind); what's playing with back / pause / next; and where the sound goes — headphones, speakers, monitor.
- **Your own services.** Any video or music site in Watch and Listen — ready-made ones in one click or your own by address; they open right here or in the browser you pick.
- **Continue.** Your last game, movie, YouTube video and music — one click away.
- **Statistics.** How long you rested today and this week, and what you played — kept only on your PC.
- **Live tiles** on the second monitor with captions: what's playing, unread messages, which page is open.
- **Schedule.** A gentle offer to rest in the evening and to get back to work in the morning.
- **English and Russian.** Follows your Windows language or the one you pick.
- **Self-update** in one click.

## Screenshots

The main window — "Work → Rest". On the left, both monitors: drag the second monitor's tiles or click two to swap them, "×" removes, "+" adds. On the right, what happens to each app: "Close · Second monitor · Leave alone"; an app you close can be set to not reopen after rest.

![Work → Rest](https://raw.githubusercontent.com/Luchano55/gigarest/screenshots/main/1-perehod.png)

The same window in English.

![English](https://raw.githubusercontent.com/Luchano55/gigarest/screenshots/main/27-english.png)

Apps close politely, one at a time — you can see which one is closing now. If one asks "Save changes?", GigaRest waits for your answer.

![Switching](https://raw.githubusercontent.com/Luchano55/gigarest/screenshots/main/2-progress.png)

The summary: how much memory was freed, what closed, what hid in the tray. Back to work is one button away (or Ctrl+Alt+Home).

![Rest](https://raw.githubusercontent.com/Luchano55/gigarest/screenshots/main/3-otdyh.png)

While you rest, the main monitor shows the Quiet screen: a scene, a clock and one line "Continue · Play · Watch · Listen · Scenes". Notifications stay silent (Do not disturb).

![Quiet screen](https://raw.githubusercontent.com/Luchano55/gigarest/screenshots/main/11-tihiy.png)

Play — Steam, Epic, GOG and Battle.net games with covers, recent ones first; a click launches the game without the launcher window.

![Play](https://raw.githubusercontent.com/Luchano55/gigarest/screenshots/main/17-igrat.png)

The whole library — every installed game from every store in one place: the last game large, search, store filters, covers.

![Library](https://raw.githubusercontent.com/Luchano55/gigarest/screenshots/main/24-biblioteka.png)

Watch — YouTube right on the Quiet screen ("Minimize" shrinks it to a corner and the video keeps playing) and movies from your folder in the built-in player.

![Watch](https://raw.githubusercontent.com/Luchano55/gigarest/screenshots/main/20-smotret.png)

Listen — music right here, what's playing with back, pause and next, where the sound goes, and nature sounds.

![Listen](https://raw.githubusercontent.com/Luchano55/gigarest/screenshots/main/19-slushat.png)

Your own services: "+ Add a service…" in Watch and Listen. Right-click a service to open it here, in your default browser or in a browser you choose.

![Add a service](https://raw.githubusercontent.com/Luchano55/gigarest/screenshots/main/26-servis.png)

Live tiles of allowed windows on the second monitor, with captions underneath.

![Tiles on the second monitor](https://raw.githubusercontent.com/Luchano55/gigarest/screenshots/main/12-plitki.png)

In the morning — "Back to work": closed apps opened again, windows in their places.

![Back to work](https://raw.githubusercontent.com/Luchano55/gigarest/screenshots/main/4-rabota.png)

Choose your work apps in a separate window, including ones that aren't running. On first start GigaRest suggests them itself.

![Work apps](https://raw.githubusercontent.com/Luchano55/gigarest/screenshots/main/5-programmy.png)

Settings: schedule, hotkeys, Do not disturb, movies folder, update channel, language, autostart.

![Settings](https://raw.githubusercontent.com/Luchano55/gigarest/screenshots/main/6-nastroyki.png)

Rest statistics: how long you rested each day and what you played.

![Statistics](https://raw.githubusercontent.com/Luchano55/gigarest/screenshots/main/25-statistika.png)

Updates in one click, with progress in a small window.

![Update](https://raw.githubusercontent.com/Luchano55/gigarest/screenshots/main/23-obnovlenie.png)

## Install

**[Download GigaRest-Setup.exe](https://github.com/Luchano55/gigarest/releases/latest/download/GigaRest-Setup.exe)** — the latest stable version.

- No admin rights needed: GigaRest installs into your user folder and appears in the Start menu and in Installed apps.
- Windows may warn about an "Unknown publisher" — the installer has no paid code signature. Click "More info" → "Run anyway".
- On first start GigaRest offers to pick your work apps and suggests them itself.
- Without installing: [GigaRest-win-x64.zip](https://github.com/Luchano55/gigarest/releases/latest/download/GigaRest-win-x64.zip) — unpack into a regular folder and run GigaRest.exe.
- To uninstall: Settings → Apps → GigaRest → Uninstall. Your settings stay in `%AppData%\GigaRest`.

**Updates.** GigaRest checks for updates itself and offers them in one click (or the tray icon → "Check for updates"). In Settings you can choose stable versions or test builds — everything new right away, but there may be bugs. The [test build](https://github.com/Luchano55/gigarest/releases/tag/dev-build) is updated after every checked build.

## Privacy

- Works offline. GigaRest itself only goes online to check GitHub for updates. Built-in sites (YouTube, Yandex Music, your services) open only when you open them.
- No telemetry and no accounts.
- No admin rights; window titles and personal data are never written to the log.

## FAQ

**What if an app asks "Save changes?"** — GigaRest waits for your answer and never answers for you. "Cancel" keeps the app open.

**Do Steam or Battle.net pop up on top?** — Steam starts the game right away without its window; the Battle.net main window minimizes itself.

**A browser tile shows only an icon?** — Some browsers stop drawing a window that is covered by another window. This is a known limitation; a fix is planned.

**Only one monitor?** — There are no tiles; allowed apps simply stay open.

## What's next

- Focus mode: one work app on the main monitor, a calm scene or work tiles on the second.
- Your Wallpaper Engine video wallpapers as Quiet screen scenes.
- Sleep timer and mixing nature sounds.

What's new — [docs/CHANGELOG.md](docs/CHANGELOG.md) (in Russian). License — MIT.
