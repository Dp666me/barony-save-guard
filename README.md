# Barony Save Guard

Barony deletes your save when your character dies. Barony Save Guard keeps a backup of every
save and puts it back the moment the game deletes it. It also keeps older versions, so you can
roll a save back to an earlier point.

Inspired by [BaronySaver](https://github.com/DanteFjante/BaronySaver), written from scratch.

## Windows

1. Download `BaronySaveGuard-windows.zip` from the [Releases](../../releases) page, unzip it anywhere and run `BaronySaveGuard.exe`.
   Windows may warn that the app is from an unknown publisher (it isn't code-signed):
   choose "More info" then "Run anyway".
2. A shield icon appears in the system tray. Right-click it for the menu:
   - **Restore a save...**: pick any backed-up version and put it back.
   - **Pause auto-restore**: stop putting deleted saves back (backups continue).
   - **Start with Windows**: launch automatically when you log in.
   - **Change save folder...**: if your saves weren't found automatically.
   - **Show activity** / **Open backups folder**.
3. Play as normal. When you die, go back to the main menu and your save is there again.

### Start it with Barony from Steam

In Steam, right-click Barony, choose **Properties**, and in **Launch Options** enter the path to
the app followed by `%command%`, for example:

```
"C:\Games\BaronySaveGuard\BaronySaveGuard.exe" %command%
```

Launching Barony from Steam then starts Save Guard too, and it closes a few seconds after the
game does. If Save Guard is already running (for example with "Start with Windows" on), the game
just starts normally and the running copy protects it.

The save folder is found automatically from the running game, or from the usual Steam, Epic
and GOG install folders. It's remembered once found.

## Linux / Steam Deck (console version)

Download `barony-save-guard-linux.tar.gz` from the [Releases](../../releases) page.

```
chmod +x barony-save-guard
./barony-save-guard                     # watch and auto-restore (Ctrl+C to stop)
./barony-save-guard --folder <path>     # if the save folder isn't found automatically
./barony-save-guard --no-restore        # back up only
./barony-save-guard list                # show backups
./barony-save-guard restore savegame0.baronysave 3   # put back version 3 from "list"
```

To start it with the game from Steam, set Barony's launch options to
`/full/path/to/barony-save-guard %command%`.

### Multiplayer

Every player keeps their own copy of a multiplayer run, and Barony deletes all of them when the whole
team dies. To load the run again, each player needs their copy back, so **every player should run
Save Guard**, not only the host.

## Where things are kept

- Backups: `%LOCALAPPDATA%\BaronySaveGuard\backups\<save name>\<timestamp>.baronysave`
  (Linux: `~/.local/share/BaronySaveGuard/backups`). The newest 30 versions of each save are kept.
- Settings: `settings.json` and an activity `log.txt` in the same `BaronySaveGuard` folder.
  `settings.json` lets you change `MaxVersionsPerSave` or `BackupFolder`.

## How it works

- A file watcher spots save changes instantly; a rescan every 3 seconds catches anything missed.
- Each change is copied to the backup folder (identical copies are skipped).
- When a save disappears, the app waits 1.5 seconds. If the game has written a new save in that
  slot meanwhile (you started a new run), the new save is kept and backed up. Otherwise the
  latest backup is put back. It never overwrites a file that exists.
- Restoring a version by hand first backs up whatever is there, so nothing is lost.

## Building from source

Requires the .NET 8 SDK.

```
dotnet test
dotnet publish src/BaronySaveGuard.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
dotnet publish src/BaronySaveGuard.Cli -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
```

Projects: `BaronySaveGuard.Core` (backup engine, cross-platform), `BaronySaveGuard.App`
(Windows tray app), `BaronySaveGuard.Cli` (console version), `tests/BaronySaveGuard.Tests`.

## License

MIT. See [LICENSE](LICENSE).
