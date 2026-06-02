# Android Kotlin TODO

Native Kotlin / Jetpack Compose Android client, porting the MAUI version's save-sync flow with direct filesystem access via `MANAGE_EXTERNAL_STORAGE`.

## Done

- [x] Gradle project with Kotlin 2.1.0, AGP 8.7.3, Compose BOM 2024.12
- [x] Data models with `kotlinx.serialization` matching C# config schema
- [x] S3-compatible cloud storage with AWS4-HMAC-SHA256 signing via OkHttp
- [x] Archive create/restore matching C# `ArchiveTransferService` format (unit-id staging)
- [x] Single-unit archive flattened to zip root (avoid `0/` wrapper directory)
- [x] Config seeding from assets on first launch
- [x] Runtime device ID resolution (`forced_device_name` from `GameSaveManager.runtime.json`)
- [x] Favorites via SharedPreferences
- [x] Config normalization on startup (merge duplicate same-type save units)
- [x] Game list with favorites bar, add game, edit Android paths
- [x] Cloud upload current / restore current / refresh status
- [x] SAF file/folder picker → real path resolution via `StoragePathResolver`
- [x] `MANAGE_EXTERNAL_STORAGE` permission with runtime request on API 30+
- [x] cleartext HTTP in AndroidManifest for HTTP S3 endpoints
- [x] Compose UI: header, add-game panel, favorites strip, all-games list, detail panel
- [x] Shizuku integration for accessing restricted paths (`/Android/data/`, `/data/data/`)
- [x] Shizuku status indicator in header (not installed / no permission / ADB / root)
- [x] Pure Zip mode: `SaveUnitType.Zip`, `linked_unit_ids`, skip pack/unpack, upload/restore file picker
- [x] Adaptive icon (mipmap-anydpi-v26 vector)

## Pitfalls & fixes

### 1. `kotlinx.serialization` strict required fields

`LegacyGameBackups` used non-nullable `Name` and `Backups` without defaults. When the S3 server returned Backups.json missing these fields (empty manifest), deserialization threw:

```
Field [Name, Backups] are required for type with serial name ... but they were missing at path: $
```

**Fix**: Added `= ""` and `= emptyList()` defaults to all cloud model fields. Also added post-parse normalization in `S3CloudService.tryLoadGameBackups()` to fill blank `Name` with the game name — matching C# `NormalizeGameBackups()`.

### 2. JSON field name mismatch: PascalCase vs snake_case

C# models use `[JsonPropertyName("snake_case")]` annotations (e.g. `"backups"`, `"device_heads"`, `"sync_version"`, `"last_known_local_head"`). Kotlin models initially used the C# property names directly (`Backups`, `DeviceHeads`, `SyncVersion`), which don't match the actual JSON keys.

**Fix**: Added `@SerialName` annotations to all `CloudModels.kt` data classes matching the exact C# `[JsonPropertyName]` values. `LegacySyncState` fields also had completely different names (`Version` → `SchemaVersion`, `LocalHead` → `LastKnownLocalHead`, `Status` → `LastSyncResult`, etc.).

### 3. `content://` URIs incompatible with `java.io.File`

SAF pickers (`OpenDocument` / `OpenDocumentTree`) return `content://` URIs. These cannot be used with `java.io.File` for archive create/restore, causing `FileNotFoundException` or silent failures.

**Fix**: Created `StoragePathResolver` that resolves `content://` URIs to real filesystem paths by parsing `DocumentsContract` document IDs (e.g. `primary:Documents/MyGame` → `/storage/emulated/0/Documents/MyGame`). Picker callbacks now resolve before storing the path. If resolution fails, the user is prompted to type the real path manually.

### 4. `compileSdk = 36` warning with AGP 8.7.3

AGP 8.7.3 was tested up to compileSdk 35. Using 36 triggers a build warning.

**Fix**: Added `android.suppressUnsupportedCompileSdk=36` to `gradle.properties`. AGP upgrade deferred until a version that officially supports API 36.

### 5. `RunBusyAsync` nesting deadlock (MAUI pitfall #4 — already prevented)

The MAUI version had `IsBusy` guarding preventing inner calls. Kotlin version uses the same pattern: public methods call `runBusy { ... }`, while internal calls go through non-guarded `reloadCore()` / `refreshCloudCore()`. No deadlock.

### 6. Single-unit archive wrapper directory (MAUI pitfall #13 — already handled)

C# `ArchiveTransferService` stages content under `{unitId}/` directories. Single-unit archives would produce an extra wrapper folder at the restore target.

**Fix**: `ArchiveService.createCurrentDeviceArchive` now flattens single-unit archives (content goes directly in zip root). Multi-unit archives keep the `{unitId}/` structure. Restore side already handled both formats via `resolveSourceRoot()`.

### 7. Duplicate same-type save units from PC/Android path split (MAUI pitfall #11 — already handled)

When PC and Android paths are stored as separate `save_paths` entries of the same type, backup/restore treats them as two independent units.

**Fix**: 
- Path editing prefers merging into an existing same-type unit instead of always creating new ones.
- `ConfigService.normalizeConfig()` merges duplicate same-type units on every startup.
- `WorkspaceService.ensureConfigPath()` calls normalization every launch (not just first seed).

### 8. Old local config surviving reinstall (MAUI pitfall #12 — already handled)

Reinstalling a newer APK does not update the local config because seeding only runs when the file doesn't exist.

**Fix**: `WorkspaceService.ensureConfigPath()` always calls `ConfigService.normalizeConfig()` on the existing config, so stale mobile configs get cleaned up on every startup.

### 9. `menuAnchor()` deprecation in Compose

`Modifier.menuAnchor()` is deprecated in newer Compose versions. The build produces a warning.

**Fix**: Pending. Replace with `menuAnchor(MenuAnchorType.PrimaryNotEditable, enabled = true)`.

### 10. Shizuku Maven coordinates: artifact name is `api`, not `shizuku-api`

The official Shizuku-API README lists the dependency as `dev.rikka.shizuku:api:VERSION`. Multiple wrong guesses were tried first: `dev.rikka.shizuku:shizuku-api` (nonexistent), `com.github.RikkaApps.Shizuku:shizuku-api` (JitPack, build never completed), various invalid version numbers. The correct artifact is published on **Maven Central** as `dev.rikka.shizuku:api` and `dev.rikka.shizuku:provider`.

**Fix**: Use `implementation("dev.rikka.shizuku:api:13.1.5")` + `implementation("dev.rikka.shizuku:provider:13.1.5")`. Gradle resolves transitive dependencies (`aidl`, `shared`) automatically.

### 11. `Shizuku.newProcess()` is private in API v13+

The Shizuku API v13 deprecated `newProcess()` in favor of `UserService` (AIDL-based inter-process service) and made it `private`. Direct calls fail with `Cannot access 'static fun newProcess': it is private`.

**Fix**: Use Java reflection to bypass the access restriction. `ShizukuHelper` calls `method.isAccessible = true` on the private `newProcess` method. The official alternative is `UserService`, which requires AIDL definitions and a separate service process.

### 12. Shizuku privilege levels: ADB (UID 2000) vs Root (UID 0)

Shizuku started via wireless debugging (ADB) runs as UID 2000. This **cannot** access `/data/data/<other.app>/`. However, `/storage/emulated/0/Android/data/` and `/sdcard/Android/data/` **can** be accessed with ADB-level Shizuku.

### 13. IsRestrictedPath should cover all `/Android/data/` variants

Different devices mount storage at different paths: `/storage/emulated/0/Android/data/`, `/sdcard/Android/data/`, `/storage/XXXX-XXXX/Android/data/` (SD cards).

**Fix**: Use `path.contains("/Android/data/") || path.contains("/Android/obb/")` to catch all variants.

### 14. Restore should use latest cloud backup, not current Android head

For cross-device sync, Android often restores a newer PC upload. `DuskLight` exposed this because Android's device head was `2026-05-29_22-04-18`, while the newest cloud backup was `2026-06-02_23-25-48` from PC. Restoring Android's own head looked like a failed restore.

**Fix**: Kotlin `restoreCurrentSave()` now resolves the latest backup globally. UI/status copy says `Restore latest` / `latest cloud save`.

## Build & deploy

```bash
export ANDROID_HOME="$HOME/AppData/Local/Android/Sdk"
export JAVA_HOME="F:/Program Files/Android/Android Studio/jbr"
cd src/EflayGameSaveManager.Kotlin
./gradlew assembleDebug
adb install -r app/build/outputs/apk/debug/app-debug.apk
```

Build takes ~30s (incremental) to ~90s (clean). APK size ~17MB.

## Next items — UI Rework

### Navigation: ModalNavigationDrawer
- [ ] Replace single-page ScrollView layout with `ModalNavigationDrawer` + top bar
- [ ] Drawer slides from left, contains: Favorites, All Games, Config Editor
- [ ] Hamburger icon in top bar to toggle drawer

### Favorites tab
- [ ] Read favorites from `GameSaveManager.config.json` → `favorites` array (instead of SharedPreferences)
- [ ] Filter games by `favorites[].label` matching game `name`
- [ ] Quick cloud sync actions per favorited game

### All Games tab
- [ ] Move current full game list here
- [ ] Keep favorites star toggle (updates config `favorites` array)

### Config Editor tab
- [ ] `OutlinedTextField` showing raw `GameSaveManager.config.json` content
- [ ] "Save" button → validate JSON → write to config file → reload
- [ ] "Import" button → SAF file picker (`application/json`) → replace config → reload

### Game Detail (shared across tabs)
- [ ] Tapping a game shows detail panel (inline or bottom sheet)
- [ ] Cloud status, upload, restore actions
- [ ] Zip mode: upload file picker / restore save dialog

## Next items — Backend

- [ ] Replace deprecated `menuAnchor()` with typed overload
- [ ] Add download-selected-backup support (not only restore-current)
- [ ] Add conflict messaging when cloud head and local head differ
- [ ] Upgrade Shizuku from `newProcess` reflection to proper `UserService`
- [ ] Remove debug logging for release builds
