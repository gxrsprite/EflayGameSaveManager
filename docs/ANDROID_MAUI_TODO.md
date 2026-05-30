# Android MAUI TODO

Current first-pass scope is file-save and folder-save cloud sync with a favorites bar for Android-focused games.

## Done

- [x] MAUI project targeting `net10.0-android` with Core lib integration
- [x] Game list with favorites, add game, edit Android paths
- [x] Cloud upload current / restore current via S3
- [x] File picker and folder picker via MAUI storage APIs
- [x] Android manifest with cleartext HTTP, storage permissions
- [x] `EmbedAssembliesIntoApk=true` for Debug (Fast Deployment not suitable for adb install)
- [x] Shizuku integration for elevated access to `/Android/data/` (via JNI interop + `#if ANDROID` ViewModel bridging)
- [x] Shizuku JARs bundled: `api`, `aidl`, `shared`, `provider` v13.1.5 from Maven Central

## Pitfalls & fixes

1. **HTTP blocked on Android 9+**: S3 endpoint uses plain `http://`, Android blocks cleartext by default. Fix: `Platforms/Android/AndroidManifest.xml` with `android:usesCleartextTraffic="true"`.

2. **Config overwritten on every Reload**: `MobileWorkspaceService.EnsureConfigPathAsync` seeded from package assets unconditionally, wiping user changes. Fix: only seed when file doesn't exist.

3. **Fast Deployment crash**: Debug builds don't embed assemblies in APK; `adb install` doesn't push the override dir, causing "No assemblies found" abort. Fix: `<EmbedAssembliesIntoApk>true</EmbedAssembliesIntoApk>` in csproj.

4. **`RunBusyAsync` nesting deadlock**: `IsBusy` guard in `RunBusyAsync` prevents inner calls (e.g., `SaveSelectedGamePathsAsync` → `ReloadAsync`). Fix: extracted `ReloadCoreAsync()` and `RefreshCloudCoreAsync()` as non-guarded private methods.

5. **Storage permission on Android 16 (API 36)**: `WRITE_EXTERNAL_STORAGE` no-ops on API 30+. Need `MANAGE_EXTERNAL_STORAGE` with runtime request via `Settings.ActionManageAllFilesAccessPermission`. Added in `MainActivity.OnCreate`.

6. **`PreserveFileTime` in SharpCompress extraction**: `ExtractArchive` used `PreserveFileTime = true`, causing restored files to keep original zip timestamps instead of current time. Fix: set to `false`.

7. **MAUI app startup XAML crash**: `MainPage` was resolved from DI before `App.xaml` resources were initialized, causing `StaticResource not found for key SurfaceColor`. Fix: inject `IServiceProvider` into `App`, then resolve `MainPage` inside `CreateWindow()` after `InitializeComponent()`.

8. **`Add game` looked unresponsive on phone**: the add form was rendered at the bottom of a long page, so tapping `Add game` changed state but did not reveal the form in the visible viewport. Fix: move the add form near the top section so it appears immediately below the header card.

9. **Android UI was reading PC fallback paths**: `Core` resolves current-device paths with fallback semantics, which is useful on desktop but misleading on Android. The MAUI layer now shows and edits only the Android device's explicit path entry instead of inherited fallback values.

10. **Cloud upload included internal restore copies**: after restore, Android could end up with an app-private fallback copy such as `/data/user/0/com.eflay.gamesavemanager.maui/files/.config...`, and that path could be archived if it appeared as a current-device path. Fix: Android cloud upload/restore now builds an Android-scoped snapshot that keeps only explicitly configured paths for the current Android device.

11. **Single logical save split into two save units**: for games like `DuskLight`, PC and Android paths were stored as separate `save_paths` entries (`id 0`, `id 1`) even though they represent one logical save slot. That made backup/restore behave like there were two independent units. Fixes:
   - Root config was updated to the intended structure: one `Folder` unit with per-device paths.
   - MAUI path saving now prefers merging into an existing same-type unit instead of creating a new duplicate unit.
   - Startup normalization now merges compatible duplicate save units in the local mobile config.

12. **Old local config surviving reinstall**: the packaged `GameSaveManager.config.json` is copied only when the local file does not exist, so reinstalling a newer APK does not automatically replace a stale mobile config. Fix: `MobileWorkspaceService` now normalizes local config on startup instead of assuming reinstall will refresh the file.

13. **Archive format leaked unit-id folders into restore target**: `ArchiveTransferService.CreateCurrentDeviceArchive()` stages content under `0/`, `1/`, etc. When a single-unit Android backup was restored, users could end up with an extra `1` directory in the target save folder. Fix: single-unit archives are now flattened so zip root contains save content directly; restore stays backward-compatible with old numeric wrapper directories.

### Shizuku integration (MAUI-specific)

14. **Shizuku dependency: Maven coordinates differ from what you'd expect**

The artifact name is `dev.rikka.shizuku:api` (NOT `shizuku-api`). The `provider` module is needed for `ShizukuProvider` (AndroidManifest ContentProvider). Critically, Gradle/Maven resolves **transitive dependencies** automatically (`aidl`, `shared`), but when bundling JARs manually into a MAUI project, all four must be included: `api`, `aidl`, `shared`, `provider`.

**Fix**: Downloaded all four AARs from Maven Central, extracted `classes.jar` from each, added to `.csproj` as `<AndroidLibrary Include="libs\*.jar" Bind="false" />`. Missing the `aidl` JAR causes `ClassNotFoundException: moe.shizuku.server.IShizukuApplication$Stub` at runtime → `SIGSEGV` crash because Shizuku daemon can't deliver the binder.

15. **Namespace collision: `EflayGameSaveManager.Maui.Platforms.Android` vs `Android.Runtime`**

The MAUI platform code lives in namespace `EflayGameSaveManager.Maui.Platforms.Android`. C# namespace resolution finds `Android` as a child of this namespace first, so `Android.Runtime.JNIEnv` fails with "type or namespace 'Runtime' does not exist". Same issue with `Java.Lang.String`.

**Fix**: Short names like `JNIEnv` work fine (resolved via `using Android.Runtime`). Fully qualified references must use `global::` prefix: `global::Android.Runtime.JavaArray<T>`, `global::Java.Lang.String`.

16. **Core vs MAUI separation: Shizuku belongs in the MAUI layer**

`EflayGameSaveManager.Core` is shared with desktop (Avalonia). Shizuku is an Android-only concern. Injecting Shizuku awareness into Core would leak platform-specific code.

**Fix**: All Shizuku logic lives behind `#if ANDROID` in the MAUI ViewModel layer:
- `ShizukuInterop.cs` — JNI bridge to `rikka.shizuku.Shizuku` Java API
- `ShizukuFileService.cs` — `CopyFromRestrictedAsync()` / `CopyToRestrictedAsync()` using Shizuku shell
- `MainPageViewModel.cs` — upload/restore flow intercepts restricted paths, stages/restores via Shizuku around Core calls, then cleans up temp files. Core operates on temp paths and never knows Shizuku exists.

17. **`IsRestrictedPath` must use `Contains`, not `StartsWith`**

Android mounts storage at varying paths: `/storage/emulated/0/Android/data/`, `/sdcard/Android/data/`, `/storage/XXXX-XXXX/Android/data/`. Using `StartsWith` per prefix misses SD cards.

**Fix**: `path.Contains("/Android/data/") || path.Contains("/Android/obb/")` catches all volumes.

## Build & deploy

```bash
export ANDROID_HOME="$HOME/AppData/Local/Android/Sdk"
export JAVA_HOME="$HOME/AppData/Local/Android/Jdk"
cd src/EflayGameSaveManager.Maui
dotnet build -c Debug -f net10.0-android -p:JavaSdkDirectory="$JAVA_HOME"
adb install -r bin/Debug/net10.0-android/com.eflay.gamesavemanager.maui-Signed.apk
```

Build takes ~3-4 min (aapt2 + d8 + signing). The `SharpCompress` NU1902 warning is a known advisory, pending package update.

## Next items

- [ ] Bridge `content://` URIs into the save archive and restore pipeline so modern Android shared storage works reliably.
- [ ] Verify the latest single-unit archive on device by downloading and inspecting the produced zip structure after upload.
- [ ] Add explicit diagnostics in the MAUI UI/logs showing which concrete paths were included in the current upload.
- [ ] Add per-game path editing after creation (delete save units, reorder).
- [ ] Add conflict messaging when cloud head and local head differ.
- [ ] Add download-selected-backup support, not only restore-current.
- [ ] Add Switch emulator zip sync:
  - Android uploads emulator-exported save zip directly to cloud.
  - PC chooses folder target and auto-extracts on restore.
  - Add zip structure validation and friendly error messages.
- [ ] Support SAF document-tree URIs for folder picker on Android 11+.
- [ ] Replace `newProcess` reflection with Shizuku `UserService` (AIDL-based, avoids private API)
- [ ] Add Shizuku-based directory browser for `/Android/data/` path selection
