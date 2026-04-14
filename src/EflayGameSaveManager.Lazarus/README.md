# Eflay Game Save Manager Lite

Lightweight Lazarus/FreePascal front end for Winlator.

Scope:

- Reads the existing `GameSaveManager.config.json`.
- Uses `GameSaveManager.runtime.json` `forced_device_name` when present.
- Lists games.
- Edits current-device save paths and game executable path.
- Backs up the selected game's current-device saves to `backup_path`.
- Shows cloud backup status through `GameSaveManager.CloudTool.exe`.
- Uploads the selected game's current save to cloud.
- Restores the selected game's current cloud save.
- Opens game/save/config folders.
- Starts the selected game executable.

The Lazarus window stays lightweight. Cloud work is delegated to `GameSaveManager.CloudTool.exe` so the app can reuse the existing S3 and zip/restore logic without reimplementing it in Pascal.

Build with Lazarus:

```text
Open GameSaveManagerLite.lpi, select Release, then Build.
```

Command line build when `lazbuild` is available:

```powershell
lazbuild --build-mode=Release src\EflayGameSaveManager.Lazarus\GameSaveManagerLite.lpi
```

Build the cloud helper:

```powershell
dotnet publish src\EflayGameSaveManager.CloudTool\EflayGameSaveManager.CloudTool.csproj -c Release -r win-x64 --self-contained true -o src\EflayGameSaveManager.Lazarus\bin
```

Run from `src\EflayGameSaveManager.Lazarus\bin` so `GameSaveManagerLite.exe` and `GameSaveManager.CloudTool.exe` are next to each other.
