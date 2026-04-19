# Eflay Game Save Manager Lite

Lightweight Lazarus/FreePascal front end for Winlator.

Scope:

- Reads the existing `GameSaveManager.config.json`.
- Uses optional `GameSaveManagerLite.config.json` for Lite-only settings.
- Supports `forced_device_name` override in Lite config (higher priority).
- Falls back to `GameSaveManager.runtime.json` `forced_device_name` when Lite config does not set it.
- Lists games.
- Edits current-device save paths and game executable path.
- Backs up the selected game's current-device saves to `backup_path`.
- Implements cloud status/upload/restore in Lazarus directly (S3-compatible signing and requests).
- Opens game/save/config folders.
- Starts the selected game executable.

Cloud restore extraction uses an external 7-Zip executable. Supported lookup order:

- `7zz.exe` (recommended, standalone)
- `7za.exe` (standalone)
- `7z.exe` + `7z.dll` (same directory, matching architecture)

Lite config settings:

- `settings.forced_device_name`: optional current-device name override (Lite-only, higher priority than runtime file)
- `settings.seven_zip_dir`: optional 7-Zip directory override
- If `seven_zip_dir` is not set, 7-Zip is resolved from the same directory as `GameSaveManagerLite.exe`

Example `GameSaveManagerLite.config.json`:

```json
{
  "settings": {
    "forced_device_name": "MyWinlatorDevice",
    "seven_zip_dir": ".\\tools\\7zip"
  }
}
```

Build with Lazarus:

```text
Open GameSaveManagerLite.lpi, select Release, then Build.
```

Command line build when `lazbuild` is available:

```powershell
lazbuild --build-mode=Release src\EflayGameSaveManager.Lazarus\GameSaveManagerLite.lpi
```

Run from `src\EflayGameSaveManager.Lazarus\bin` and ensure a supported 7-Zip executable is available.
