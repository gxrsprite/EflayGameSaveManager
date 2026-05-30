# EflayGameSaveManager.Maui

Experimental MAUI client for the Android-first save-sync flow.

Current scope:

- Reuses the existing `EflayGameSaveManager.Core` configuration and S3-compatible cloud sync model.
- Focuses on file-save and folder-save games.
- Adds a favorites strip for the games you care about on Android.
- Keeps Switch emulator zip sync out of the first pass and tracks it in `docs/ANDROID_MAUI_TODO.md`.

Notes:

- The app seeds `GameSaveManager.config.json` and `GameSaveManager.runtime.json` into `FileSystem.AppDataDirectory` on first launch.
- The app also normalizes the local mobile config on startup so duplicate same-type save units for different devices can be merged into one logical unit.
- Cloud layout stays unchanged and continues to use `root_path/save_data/{gameName}/{timestamp}.zip` plus `Backups.json` and `sync_state.json`.
- Android cloud sync is scoped to explicit Android device paths only; MAUI should not upload desktop fallback paths or app-private restore copies.
- Single-unit archives are expected to restore without creating an extra `0`/`1` wrapper folder in the target save directory.
- Android SAF and document-tree bridging are still needed for the broadest storage compatibility on modern devices. This first pass is the MAUI shell and workflow baseline.
