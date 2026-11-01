# EflayGameSaveManager.Maui

Experimental MAUI client for the Android-first save-sync flow.

Current scope:

- Reuses the existing `EflayGameSaveManager.Core` configuration and S3-compatible cloud sync model.
- Supports file-save, folder-save and configured ZIP-save games.
- Adds a favorites strip for the games you care about on Android.
- The add-game and path-edit forms have a ZIP switch and a persistent ZIP file selector.
- Both forms expose the same `contents_only` switch for Folder and ZIP. ZIP configuration preserves the flag; upload, local backup and ZIP export use Core's `ZipSaveLayoutService`. Folder/ZIP cross-device transfers and repeated numeric-subdirectory round trips are tested; see `docs/ZIP_FOLDER_LAYOUT.md`.
- Uses a light palette matching the Kotlin client. Cloud backups show their date, device name, size and description; select a backup to edit its description.
- NFS9 / Most Wanted (2005) PC-format personal saves can be chosen and edited on Android, including alias, money, total bounty, and the current Blacklist stage's Boss Ready progress. Close the game first. Both ordinary paths and Shizuku-accessible restricted paths retain a backup before the edited save is written.
- The Config page can export the current MAUI configuration via the system file picker for import into the Kotlin client.

Notes:

- The app seeds `GameSaveManager.config.json` and `GameSaveManager.runtime.json` into `FileSystem.AppDataDirectory` on first launch.
- The app also normalizes the local mobile config on startup so duplicate same-type save units for different devices can be merged into one logical unit.
- Mobile config migration preserves `contents_only`, exclusions and stable unit IDs. Units with different content modes or exclusions are not merged, and linked units keep their identities. Cult of the Lamb folder units are migrated to contents-only mode, repairing configs affected by the old clone that dropped the flag.
- `MobileConfigNormalizer` and `AndroidGameSnapshotFactory` have no MAUI runtime dependency and are linked into desktop-hosted regression tests. The same factory scopes both upload and restore snapshots after Shizuku path redirection while preserving all save-unit metadata.
- Cloud layout stays unchanged and continues to use `root_path/save_data/{gameName}/{timestamp}.zip` plus `Backups.json` and `sync_state.json`.
- Android cloud sync is scoped to explicit Android device paths only; MAUI should not upload desktop fallback paths or app-private restore copies.
- Shizuku calls use managed Java peers and reflected `Method.Accessible`, with no manual JNI reference deletion. Both process output pipes are drained concurrently.
- Restricted-path upload staging fails visibly if any save unit cannot be read, restores redirected config paths on failure, and isolates each unit under a temporary staging directory.
- Restricted restore also restores original config paths in its `finally` block, so the next backup reads the real game directory rather than a deleted restore cache.
- Restricted folder restore stages into `unit-N/<original-folder-name>` so Core can match a folder-inclusive archive's wrapper to the logical target; staging into just `unit-N` would incorrectly restore `savedata/savedata`.
- After a successful restricted folder restore, a same-named nested folder is relocated to persistent app-data `restore-layout-backups` only when `diff -qr` confirms it exactly matches the restored tree. Different/newer files and symbolic-link wrappers remain untouched; recovered data is retained rather than deleted.
- Contents-only backups use the same `0/content/...` layout in both directions; conversion changes save encoding and extensions, never the target folder nesting. Tests cover Android day-17 upload to PC, PC day-18 upload back to Android, and preservation of device-local settings.
- Single-unit archives are expected to restore without creating an extra `0`/`1` wrapper folder in the target save directory.
- Custom path selection opens an in-app browser with manual entry, parent navigation and Shizuku listing for restricted paths such as `/data/data`. Its virtualized detail rows show left-aligned names and immediate child counts (including hidden entries); inaccessible counts are shown as unavailable. Browsing does not bypass permissions; other apps' private directories normally require root Shizuku.
- Configured ZIP selection uses Android `ACTION_OPEN_DOCUMENT` with persistent read/write URI grants. Upload reads the current document rather than an old cache copy; restore writes to that document after retaining its old contents under app-data `zip-before-restore`. Legacy ZIP units without an Android path retain the pick-on-upload/save-as workflow.
- `MobileSaveUnitEditor` isolates changes to the Android device's unit type and links the new unit to its previous folder unit, preserving PC paths and unit metadata. Its persistence and mode-switch behavior are covered by desktop-hosted tests.
- SAF document-tree support for general folder paths remains pending.
