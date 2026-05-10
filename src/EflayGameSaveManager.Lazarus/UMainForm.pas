unit UMainForm;

{$mode objfpc}{$H+}

interface

uses
  Classes, SysUtils, StrUtils, Forms, Controls, StdCtrls, ExtCtrls, Grids, Dialogs,
  Process, fpjson, jsonparser, zipper, UPascalS3Client;

type
  TMainForm = class(TForm)
  private
    FConfigPath: string;
    FConfig: TJSONObject;
    FInitialized: Boolean;
    FCurrentDeviceId: string;
    FCurrentDeviceName: string;
    FSelectedGame: TJSONObject;
    FGameList: TListBox;
    FStatus: TLabel;
    FInfo: TMemo;
    FGamePathEdit: TEdit;
    FSaveGrid: TStringGrid;
    FReloadButton: TButton;
    FSaveButton: TButton;
    FBackupButton: TButton;
    FCloudStatusButton: TButton;
    FCloudUploadButton: TButton;
    FCloudRestoreButton: TButton;
    FCloudRebuildButton: TButton;
    FRunButton: TButton;
    FOpenGameFolderButton: TButton;
    FOpenSaveFolderButton: TButton;
    FSelectGamePathButton: TButton;
    FSelectSavePathButton: TButton;
    FOpenConfigButton: TButton;
    FStartupTimer: TTimer;
    procedure BuildUi;
    procedure StartupTimerTick(Sender: TObject);
    procedure LoadConfig;
    procedure LoadGames;
    procedure SelectGame(Index: Integer);
    procedure UpdateSelectedGameDetails;
    procedure SaveCurrentDevicePaths;
    procedure BackupSelectedGame;
    procedure SetStatus(const Value: string);
    procedure GameSelectionChanged(Sender: TObject);
    procedure ReloadClicked(Sender: TObject);
    procedure SaveClicked(Sender: TObject);
    procedure BackupClicked(Sender: TObject);
    procedure CloudStatusClicked(Sender: TObject);
    procedure CloudUploadClicked(Sender: TObject);
    procedure CloudRestoreClicked(Sender: TObject);
    procedure CloudRebuildClicked(Sender: TObject);
    procedure RunClicked(Sender: TObject);
    procedure OpenGameFolderClicked(Sender: TObject);
    procedure OpenSaveFolderClicked(Sender: TObject);
    procedure SelectGamePathClicked(Sender: TObject);
    procedure SelectSavePathClicked(Sender: TObject);
    procedure OpenConfigClicked(Sender: TObject);
    function FindConfigPath: string;
    function ReadRuntimeForcedDeviceName(const ConfigPath: string): string;
    function ReadLiteConfigForcedDeviceName: string;
    function EnsureCurrentDevice(const ForcedDeviceName: string): string;
    function ResolvePathTokens(const Value: string): string;
    function GetCurrentDevicePath(Paths: TJSONObject): string;
    function GetCurrentGamePath(Game: TJSONObject): string;
    function GetGameName(Game: TJSONObject): string;
    function ResolveBackupRoot: string;
    function FindCloudToolPath: string;
    function SanitizeSegment(const Value: string): string;
    function IsAbsolutePath(const Value: string): Boolean;
    function JsonArray(const Obj: TJSONObject; const AName: string): TJSONArray;
    function JsonObject(const Obj: TJSONObject; const AName: string): TJSONObject;
    function SelectedSavePath: string;
    function SelectedSaveUnitType: string;
    procedure OpenFolderForPath(const PathValue: string);
    procedure CopyPathToBackup(const SourcePath, DestinationPath: string);
    procedure CopyDirectoryToBackup(const SourceDir, DestinationDir: string);
    procedure CopyFileToBackup(const SourceFile, DestinationFile: string);
    function RunRegCommand(const Args: array of string): Boolean;
    function RegistryKeyExists(const RegistryPath: string): Boolean;
    function ExportRegistryKey(const RegistryPath, DestinationFile: string): Boolean;
    function ImportRegistryFile(const RegistryFile: string): Boolean;
    function DeleteRegistryKey(const RegistryPath: string): Boolean;
    procedure RunPascalCloudStatus;
    procedure RunPascalCloudUpload;
    procedure RunPascalCloudRestore;
    procedure RunPascalCloudRebuildManifest;
    procedure RunCloudTool(const CloudAction: string);
    function Resolve7ZipExecutablePath: string;
    function ReadLiteConfigSevenZipDir: string;
    procedure ExtractArchiveWith7Zip(const ArchivePath, ExtractRoot: string);
    function CreateCurrentDeviceArchive(const WorkRoot: string): string;
    procedure StageSaveUnit(const SourcePath, StagedPath, UnitType: string);
    procedure AddDirectoryToZip(Zipper: TZipper; const RootDir, SourceDir: string);
    function ResolveCurrentCloudBackup(const Backups: TJSONArray; const DeviceHeads: TJSONObject): TJSONObject;
    function ResolveArchiveKey(const CloudSettings: TJSONObject; const Backup: TJSONObject): string;
    function ResolveExtractedUnitRoot(const ExtractRoot: string; UnitId, UnitCount: Integer): string;
    function ResolveFolderContentRoot(const SourceRoot, TargetPath: string): string;
    procedure RestoreExtractedUnit(const SourceRoot, TargetPath, UnitType: string; DeleteBeforeApply: Boolean);
    procedure DeletePathRecursive(const PathValue: string);
  public
    constructor Create(TheOwner: TComponent); override;
    destructor Destroy; override;
  end;

var
  MainForm: TMainForm;

implementation

const
  ConfigFileName = 'GameSaveManager.config.json';
  LiteConfigFileName = 'GameSaveManagerLite.config.json';
  RuntimeFileName = 'GameSaveManager.runtime.json';

constructor TMainForm.Create(TheOwner: TComponent);
begin
  inherited Create(TheOwner);
  Caption := 'Eflay Game Save Manager Lite';
  Width := 960;
  Height := 620;
  Position := poDesigned;
  Left := 80;
  Top := 80;
  WindowState := wsNormal;
  ShowInTaskBar := stAlways;
  BuildUi;
end;

destructor TMainForm.Destroy;
begin
  FreeAndNil(FConfig);
  inherited Destroy;
end;

procedure TMainForm.StartupTimerTick(Sender: TObject);
begin
  if FInitialized then
    Exit;

  FInitialized := True;
  FStartupTimer.Enabled := False;
  try
    LoadConfig;
  except
    on E: Exception do
    begin
      FInfo.Lines.Clear;
      FInfo.Lines.Add(E.Message);
      SetStatus('Failed to load configuration.');
    end;
  end;
end;

procedure TMainForm.BuildUi;
var
  Root: TPanel;
  LeftPanel: TPanel;
  RightPanel: TPanel;
  TopButtons: TPanel;
  CloudButtons: TPanel;
  LocalButtons: TPanel;
  PathPanel: TPanel;
  SavePathPanel: TPanel;
begin
  Root := TPanel.Create(Self);
  Root.Parent := Self;
  Root.Align := alClient;
  Root.BevelOuter := bvNone;
  Root.BorderSpacing.Around := 8;

  LeftPanel := TPanel.Create(Self);
  LeftPanel.Parent := Root;
  LeftPanel.Align := alLeft;
  LeftPanel.Width := 280;
  LeftPanel.Caption := '';

  FGameList := TListBox.Create(Self);
  FGameList.Parent := LeftPanel;
  FGameList.Align := alClient;
  FGameList.OnClick := @GameSelectionChanged;

  RightPanel := TPanel.Create(Self);
  RightPanel.Parent := Root;
  RightPanel.Align := alClient;
  RightPanel.Caption := '';
  RightPanel.BorderSpacing.Left := 8;

  TopButtons := TPanel.Create(Self);
  TopButtons.Parent := RightPanel;
  TopButtons.Align := alTop;
  TopButtons.Height := 86;
  TopButtons.Caption := '';

  CloudButtons := TPanel.Create(Self);
  CloudButtons.Parent := TopButtons;
  CloudButtons.Align := alTop;
  CloudButtons.Height := 42;
  CloudButtons.Caption := '';
  CloudButtons.BevelOuter := bvNone;

  LocalButtons := TPanel.Create(Self);
  LocalButtons.Parent := TopButtons;
  LocalButtons.Align := alTop;
  LocalButtons.Height := 38;
  LocalButtons.Caption := '';
  LocalButtons.BevelOuter := bvNone;

  FCloudStatusButton := TButton.Create(Self);
  FCloudStatusButton.Parent := CloudButtons;
  FCloudStatusButton.Caption := 'Cloud Status';
  FCloudStatusButton.Left := 0;
  FCloudStatusButton.Top := 4;
  FCloudStatusButton.Width := 105;
  FCloudStatusButton.Height := 32;
  FCloudStatusButton.OnClick := @CloudStatusClicked;

  FCloudUploadButton := TButton.Create(Self);
  FCloudUploadButton.Parent := CloudButtons;
  FCloudUploadButton.Caption := '↑ Upload Current Save To Cloud';
  FCloudUploadButton.Left := 112;
  FCloudUploadButton.Top := 4;
  FCloudUploadButton.Width := 195;
  FCloudUploadButton.Height := 32;
  FCloudUploadButton.OnClick := @CloudUploadClicked;

  FCloudRestoreButton := TButton.Create(Self);
  FCloudRestoreButton.Parent := CloudButtons;
  FCloudRestoreButton.Caption := '↓ Restore Current Cloud Save';
  FCloudRestoreButton.Left := 314;
  FCloudRestoreButton.Top := 4;
  FCloudRestoreButton.Width := 195;
  FCloudRestoreButton.Height := 32;
  FCloudRestoreButton.OnClick := @CloudRestoreClicked;

  FCloudRebuildButton := TButton.Create(Self);
  FCloudRebuildButton.Parent := CloudButtons;
  FCloudRebuildButton.Caption := 'Rebuild Index';
  FCloudRebuildButton.Left := 516;
  FCloudRebuildButton.Top := 4;
  FCloudRebuildButton.Width := 125;
  FCloudRebuildButton.Height := 32;
  FCloudRebuildButton.OnClick := @CloudRebuildClicked;

  FReloadButton := TButton.Create(Self);
  FReloadButton.Parent := LocalButtons;
  FReloadButton.Caption := 'Reload';
  FReloadButton.Left := 0;
  FReloadButton.Top := 4;
  FReloadButton.Width := 90;
  FReloadButton.OnClick := @ReloadClicked;

  FSaveButton := TButton.Create(Self);
  FSaveButton.Parent := LocalButtons;
  FSaveButton.Caption := 'Save Paths';
  FSaveButton.Left := 96;
  FSaveButton.Top := 4;
  FSaveButton.Width := 100;
  FSaveButton.OnClick := @SaveClicked;

  FBackupButton := TButton.Create(Self);
  FBackupButton.Parent := LocalButtons;
  FBackupButton.Caption := 'Backup Saves';
  FBackupButton.Left := 202;
  FBackupButton.Top := 4;
  FBackupButton.Width := 100;
  FBackupButton.OnClick := @BackupClicked;

  FRunButton := TButton.Create(Self);
  FRunButton.Parent := LocalButtons;
  FRunButton.Caption := 'Run Game';
  FRunButton.Left := 308;
  FRunButton.Top := 4;
  FRunButton.Width := 90;
  FRunButton.OnClick := @RunClicked;

  FOpenConfigButton := TButton.Create(Self);
  FOpenConfigButton.Parent := LocalButtons;
  FOpenConfigButton.Caption := 'Config';
  FOpenConfigButton.Left := 404;
  FOpenConfigButton.Top := 4;
  FOpenConfigButton.Width := 70;
  FOpenConfigButton.OnClick := @OpenConfigClicked;

  FStatus := TLabel.Create(Self);
  FStatus.Parent := RightPanel;
  FStatus.Align := alTop;
  FStatus.Height := 24;
  FStatus.Caption := 'Loading...';

  FInfo := TMemo.Create(Self);
  FInfo.Parent := RightPanel;
  FInfo.Align := alTop;
  FInfo.Height := 130;
  FInfo.ReadOnly := True;
  FInfo.ScrollBars := ssVertical;

  PathPanel := TPanel.Create(Self);
  PathPanel.Parent := RightPanel;
  PathPanel.Align := alTop;
  PathPanel.Height := 34;
  PathPanel.Caption := '';

  FGamePathEdit := TEdit.Create(Self);
  FGamePathEdit.Parent := PathPanel;
  FGamePathEdit.Align := alClient;

  FSelectGamePathButton := TButton.Create(Self);
  FSelectGamePathButton.Parent := PathPanel;
  FSelectGamePathButton.Align := alRight;
  FSelectGamePathButton.Caption := 'Select Path';
  FSelectGamePathButton.Width := 92;
  FSelectGamePathButton.OnClick := @SelectGamePathClicked;

  FOpenGameFolderButton := TButton.Create(Self);
  FOpenGameFolderButton.Parent := PathPanel;
  FOpenGameFolderButton.Align := alRight;
  FOpenGameFolderButton.Caption := 'Open Folder';
  FOpenGameFolderButton.Width := 92;
  FOpenGameFolderButton.OnClick := @OpenGameFolderClicked;

  SavePathPanel := TPanel.Create(Self);
  SavePathPanel.Parent := RightPanel;
  SavePathPanel.Align := alTop;
  SavePathPanel.Height := 34;
  SavePathPanel.Caption := '';

  FSelectSavePathButton := TButton.Create(Self);
  FSelectSavePathButton.Parent := SavePathPanel;
  FSelectSavePathButton.Align := alRight;
  FSelectSavePathButton.Caption := 'Select Path';
  FSelectSavePathButton.Width := 92;
  FSelectSavePathButton.OnClick := @SelectSavePathClicked;

  FOpenSaveFolderButton := TButton.Create(Self);
  FOpenSaveFolderButton.Parent := SavePathPanel;
  FOpenSaveFolderButton.Align := alRight;
  FOpenSaveFolderButton.Caption := 'Open Folder';
  FOpenSaveFolderButton.Width := 92;
  FOpenSaveFolderButton.OnClick := @OpenSaveFolderClicked;

  FSaveGrid := TStringGrid.Create(Self);
  FSaveGrid.Parent := RightPanel;
  FSaveGrid.Align := alClient;
  FSaveGrid.ColCount := 3;
  FSaveGrid.FixedCols := 0;
  FSaveGrid.FixedRows := 1;
  FSaveGrid.Options := FSaveGrid.Options + [goEditing, goColSizing];
  FSaveGrid.Cells[0, 0] := 'Unit Id';
  FSaveGrid.Cells[1, 0] := 'Type';
  FSaveGrid.Cells[2, 0] := 'Current Device Save Path';
  FSaveGrid.ColWidths[0] := 70;
  FSaveGrid.ColWidths[1] := 80;
  FSaveGrid.ColWidths[2] := 520;

  FStartupTimer := TTimer.Create(Self);
  FStartupTimer.Enabled := False;
  FStartupTimer.Interval := 100;
  FStartupTimer.OnTimer := @StartupTimerTick;
  FStartupTimer.Enabled := True;
end;

procedure TMainForm.LoadConfig;
var
  Parser: TJSONParser;
  JsonText: TStringList;
  ForcedDeviceName: string;
begin
  FreeAndNil(FConfig);
  FSelectedGame := nil;
  FConfigPath := FindConfigPath;

  JsonText := TStringList.Create;
  try
    JsonText.LoadFromFile(FConfigPath);
    Parser := TJSONParser.Create(JsonText.Text);
    try
      FConfig := Parser.Parse as TJSONObject;
    finally
      Parser.Free;
    end;
  finally
    JsonText.Free;
  end;

  ForcedDeviceName := ReadLiteConfigForcedDeviceName;
  if ForcedDeviceName = '' then
    ForcedDeviceName := ReadRuntimeForcedDeviceName(FConfigPath);
  FCurrentDeviceId := EnsureCurrentDevice(ForcedDeviceName);
  LoadGames;
  SetStatus('Loaded ' + IntToStr(FGameList.Items.Count) + ' games for ' + FCurrentDeviceName);
end;

procedure TMainForm.LoadGames;
var
  Games: TJSONArray;
  I: Integer;
begin
  FGameList.Items.BeginUpdate;
  try
    FGameList.Items.Clear;
    Games := JsonArray(FConfig, 'games');
    for I := 0 to Games.Count - 1 do
      FGameList.Items.AddObject(GetGameName(Games.Objects[I]), Games.Objects[I]);
  finally
    FGameList.Items.EndUpdate;
  end;

  if FGameList.Items.Count > 0 then
  begin
    FGameList.ItemIndex := 0;
    SelectGame(0);
  end
  else
    UpdateSelectedGameDetails;
end;

procedure TMainForm.SelectGame(Index: Integer);
begin
  if (Index >= 0) and (Index < FGameList.Items.Count) then
    FSelectedGame := TJSONObject(FGameList.Items.Objects[Index])
  else
    FSelectedGame := nil;

  UpdateSelectedGameDetails;
end;

procedure TMainForm.UpdateSelectedGameDetails;
var
  SavePaths: TJSONArray;
  SaveUnit: TJSONObject;
  Paths: TJSONObject;
  GamePaths: TJSONObject;
  I: Integer;
  UnitType: string;
begin
  FInfo.Clear;
  FGamePathEdit.Text := '';
  FSaveGrid.RowCount := 1;

  if FSelectedGame = nil then
  begin
    FInfo.Lines.Add('No game selected.');
    Exit;
  end;

  SavePaths := JsonArray(FSelectedGame, 'save_paths');
  GamePaths := JsonObject(FSelectedGame, 'game_paths');
  FGamePathEdit.Text := ResolvePathTokens(GetCurrentGamePath(FSelectedGame));

  FInfo.Lines.Add('Game: ' + GetGameName(FSelectedGame));
  FInfo.Lines.Add('Config: ' + FConfigPath);
  FInfo.Lines.Add('Current device: ' + FCurrentDeviceName + ' [' + FCurrentDeviceId + ']');
  FInfo.Lines.Add('Cloud sync: ' + BoolToStr(FSelectedGame.Get('cloud_sync_enabled', False), True));
  FInfo.Lines.Add('Configured game paths: ' + IntToStr(GamePaths.Count));
  FInfo.Lines.Add('Save units: ' + IntToStr(SavePaths.Count));

  FSaveGrid.RowCount := SavePaths.Count + 1;
  for I := 0 to SavePaths.Count - 1 do
  begin
    SaveUnit := SavePaths.Objects[I];
    Paths := JsonObject(SaveUnit, 'paths');
    UnitType := SaveUnit.Get('unit_type', '');
    FSaveGrid.Cells[0, I + 1] := IntToStr(SaveUnit.Get('id', I));
    FSaveGrid.Cells[1, I + 1] := UnitType;
    FSaveGrid.Cells[2, I + 1] := ResolvePathTokens(GetCurrentDevicePath(Paths));
  end;
end;

procedure TMainForm.SaveCurrentDevicePaths;
var
  SavePaths: TJSONArray;
  SaveUnit: TJSONObject;
  Paths: TJSONObject;
  GamePaths: TJSONObject;
  I: Integer;
  JsonText: TStringList;
begin
  if FSelectedGame = nil then
    Exit;

  SavePaths := JsonArray(FSelectedGame, 'save_paths');
  for I := 0 to SavePaths.Count - 1 do
  begin
    SaveUnit := SavePaths.Objects[I];
    Paths := JsonObject(SaveUnit, 'paths');
    Paths.Strings[FCurrentDeviceId] := Trim(FSaveGrid.Cells[2, I + 1]);
  end;

  GamePaths := JsonObject(FSelectedGame, 'game_paths');
  GamePaths.Strings[FCurrentDeviceId] := Trim(FGamePathEdit.Text);

  JsonText := TStringList.Create;
  try
    JsonText.Text := FConfig.FormatJSON;
    JsonText.SaveToFile(FConfigPath);
  finally
    JsonText.Free;
  end;

  SetStatus('Saved current-device paths for ' + GetGameName(FSelectedGame));
end;

procedure TMainForm.BackupSelectedGame;
var
  SavePaths: TJSONArray;
  SaveUnit: TJSONObject;
  Paths: TJSONObject;
  BackupRoot: string;
  TargetRoot: string;
  UnitTarget: string;
  SourcePath: string;
  I: Integer;
begin
  if FSelectedGame = nil then
    Exit;

  BackupRoot := ResolveBackupRoot;
  TargetRoot := IncludeTrailingPathDelimiter(BackupRoot) +
    SanitizeSegment(GetGameName(FSelectedGame)) + DirectorySeparator +
    FormatDateTime('yyyymmdd-hhnnss', Now) + DirectorySeparator +
    SanitizeSegment(FCurrentDeviceName);
  ForceDirectories(TargetRoot);

  SavePaths := JsonArray(FSelectedGame, 'save_paths');
  for I := 0 to SavePaths.Count - 1 do
  begin
    SaveUnit := SavePaths.Objects[I];
    Paths := JsonObject(SaveUnit, 'paths');
    SourcePath := ResolvePathTokens(GetCurrentDevicePath(Paths));
    if SourcePath = '' then
      Continue;

    UnitTarget := IncludeTrailingPathDelimiter(TargetRoot) + 'unit-' + IntToStr(SaveUnit.Get('id', I));
    CopyPathToBackup(SourcePath, UnitTarget);
  end;

  SetStatus('Backed up saves to ' + TargetRoot);
end;

procedure TMainForm.SetStatus(const Value: string);
begin
  FStatus.Caption := Value;
end;

procedure TMainForm.GameSelectionChanged(Sender: TObject);
begin
  SelectGame(FGameList.ItemIndex);
end;

procedure TMainForm.ReloadClicked(Sender: TObject);
begin
  try
    LoadConfig;
  except
    on E: Exception do
      SetStatus(E.Message);
  end;
end;

procedure TMainForm.SaveClicked(Sender: TObject);
begin
  try
    SaveCurrentDevicePaths;
  except
    on E: Exception do
      SetStatus(E.Message);
  end;
end;

procedure TMainForm.BackupClicked(Sender: TObject);
begin
  try
    BackupSelectedGame;
  except
    on E: Exception do
      SetStatus(E.Message);
  end;
end;

procedure TMainForm.CloudStatusClicked(Sender: TObject);
begin
  RunPascalCloudStatus;
end;

procedure TMainForm.CloudUploadClicked(Sender: TObject);
begin
  RunPascalCloudUpload;
end;

procedure TMainForm.CloudRestoreClicked(Sender: TObject);
begin
  RunPascalCloudRestore;
end;

procedure TMainForm.CloudRebuildClicked(Sender: TObject);
begin
  RunPascalCloudRebuildManifest;
end;

procedure TMainForm.RunClicked(Sender: TObject);
var
  Proc: TProcess;
  ExePath: string;
begin
  ExePath := Trim(FGamePathEdit.Text);
  if ExePath = '' then
  begin
    SetStatus('Game executable path is empty.');
    Exit;
  end;

  if not FileExists(ExePath) then
  begin
    SetStatus('Game executable not found: ' + ExePath);
    Exit;
  end;

  Proc := TProcess.Create(nil);
  try
    Proc.Executable := ExePath;
    Proc.CurrentDirectory := ExtractFileDir(ExePath);
    Proc.Options := [];
    Proc.Execute;
    SetStatus('Started: ' + ExePath);
  finally
    Proc.Free;
  end;
end;

procedure TMainForm.OpenGameFolderClicked(Sender: TObject);
begin
  OpenFolderForPath(FGamePathEdit.Text);
end;

procedure TMainForm.OpenSaveFolderClicked(Sender: TObject);
begin
  OpenFolderForPath(SelectedSavePath);
end;

procedure TMainForm.SelectGamePathClicked(Sender: TObject);
var
  Dialog: TOpenDialog;
  SelectedPath: string;
begin
  if FSelectedGame = nil then
    Exit;

  Dialog := TOpenDialog.Create(Self);
  try
    Dialog.Title := 'Select Game Executable';
    Dialog.Filter := 'Executable files|*.exe;*.bat;*.cmd|All files|*.*';
    SelectedPath := Trim(FGamePathEdit.Text);
    if FileExists(SelectedPath) then
    begin
      Dialog.InitialDir := ExtractFileDir(SelectedPath);
      Dialog.FileName := ExtractFileName(SelectedPath);
    end
    else if DirectoryExists(SelectedPath) then
      Dialog.InitialDir := SelectedPath;

    if Dialog.Execute then
    begin
      FGamePathEdit.Text := Dialog.FileName;
      SaveCurrentDevicePaths;
    end;
  finally
    Dialog.Free;
  end;
end;

procedure TMainForm.SelectSavePathClicked(Sender: TObject);
var
  Dialog: TOpenDialog;
  SelectedPath: string;
begin
  if FSelectedGame = nil then
    Exit;

  if FSaveGrid.RowCount <= 1 then
  begin
    SetStatus('No save path is available for the selected game.');
    Exit;
  end;

  if FSaveGrid.Row <= 0 then
    FSaveGrid.Row := 1;

  if SameText(SelectedSaveUnitType, 'File') then
  begin
    Dialog := TOpenDialog.Create(Self);
    try
      Dialog.Title := 'Select Save File';
      SelectedPath := SelectedSavePath;
      if FileExists(SelectedPath) then
      begin
        Dialog.InitialDir := ExtractFileDir(SelectedPath);
        Dialog.FileName := ExtractFileName(SelectedPath);
      end
      else if DirectoryExists(SelectedPath) then
        Dialog.InitialDir := SelectedPath;

      if Dialog.Execute then
      begin
        FSaveGrid.Cells[2, FSaveGrid.Row] := Dialog.FileName;
        SaveCurrentDevicePaths;
      end;
    finally
      Dialog.Free;
    end;
  end
  else
  begin
    SelectedPath := SelectedSavePath;
    if SelectDirectory('Select Save Folder', '', SelectedPath) then
    begin
      FSaveGrid.Cells[2, FSaveGrid.Row] := SelectedPath;
      SaveCurrentDevicePaths;
    end;
  end;
end;

procedure TMainForm.OpenConfigClicked(Sender: TObject);
begin
  OpenFolderForPath(FConfigPath);
end;

function TMainForm.FindConfigPath: string;
var
  Dir: string;
  Candidate: string;
begin
  Dir := ExtractFileDir(ParamStr(0));
  while Dir <> '' do
  begin
    Candidate := IncludeTrailingPathDelimiter(Dir) + ConfigFileName;
    if FileExists(Candidate) then
      Exit(Candidate);

    if ExtractFileDir(ExcludeTrailingPathDelimiter(Dir)) = Dir then
      Break;

    Dir := ExtractFileDir(ExcludeTrailingPathDelimiter(Dir));
  end;

  Candidate := ExpandFileName('..\..\' + ConfigFileName);
  if FileExists(Candidate) then
    Exit(Candidate);

  raise Exception.Create('Could not locate ' + ConfigFileName);
end;

function TMainForm.ReadRuntimeForcedDeviceName(const ConfigPath: string): string;
var
  RuntimePath: string;
  Parser: TJSONParser;
  RuntimeJson: TJSONObject;
  JsonText: TStringList;
begin
  Result := '';
  RuntimePath := IncludeTrailingPathDelimiter(ExtractFileDir(ConfigPath)) + RuntimeFileName;
  if not FileExists(RuntimePath) then
    Exit;

  JsonText := TStringList.Create;
  try
    JsonText.LoadFromFile(RuntimePath);
    Parser := TJSONParser.Create(JsonText.Text);
    try
      RuntimeJson := Parser.Parse as TJSONObject;
      try
        Result := Trim(RuntimeJson.Get('forced_device_name', ''));
      finally
        RuntimeJson.Free;
      end;
    finally
      Parser.Free;
    end;
  finally
    JsonText.Free;
  end;
end;

function TMainForm.ReadLiteConfigForcedDeviceName: string;
var
  LiteConfigPath: string;
  JsonText: TStringList;
  Parser: TJSONParser;
  Root: TJSONObject;
  Settings: TJSONObject;
begin
  Result := '';
  LiteConfigPath := IncludeTrailingPathDelimiter(ExtractFileDir(FConfigPath)) + LiteConfigFileName;
  if not FileExists(LiteConfigPath) then
    Exit;

  JsonText := TStringList.Create;
  try
    JsonText.LoadFromFile(LiteConfigPath);
    Parser := TJSONParser.Create(JsonText.Text);
    try
      Root := Parser.Parse as TJSONObject;
      try
        Settings := Root.Objects['settings'];
        if Settings <> nil then
          Result := Trim(Settings.Get('forced_device_name', ''));
      finally
        Root.Free;
      end;
    finally
      Parser.Free;
    end;
  finally
    JsonText.Free;
  end;
end;

function TMainForm.EnsureCurrentDevice(const ForcedDeviceName: string): string;
var
  Devices: TJSONObject;
  Device: TJSONObject;
  I: Integer;
  Id: string;
  Guid: TGUID;
begin
  Devices := JsonObject(FConfig, 'devices');
  if ForcedDeviceName <> '' then
    FCurrentDeviceName := ForcedDeviceName
  else
    FCurrentDeviceName := GetEnvironmentVariable('COMPUTERNAME');

  if FCurrentDeviceName = '' then
    FCurrentDeviceName := 'Winlator';

  for I := 0 to Devices.Count - 1 do
  begin
    Device := Devices.Items[I] as TJSONObject;
    if SameText(Device.Get('name', ''), FCurrentDeviceName) then
      Exit(Device.Get('id', Devices.Names[I]));
  end;

  CreateGUID(Guid);
  Id := LowerCase(GUIDToString(Guid));
  Id := StringReplace(Id, '{', '', [rfReplaceAll]);
  Id := StringReplace(Id, '}', '', [rfReplaceAll]);

  Device := TJSONObject.Create;
  Device.Add('id', Id);
  Device.Add('name', FCurrentDeviceName);
  Devices.Add(Id, Device);
  Result := Id;
end;

function TMainForm.ResolvePathTokens(const Value: string): string;
var
  Home: string;
begin
  Result := Value;
  Home := GetEnvironmentVariable('USERPROFILE');
  if Home = '' then
    Home := GetEnvironmentVariable('HOME');

  Result := StringReplace(Result, '<home>', Home, [rfReplaceAll, rfIgnoreCase]);
  Result := StringReplace(Result, '<winDocuments>', IncludeTrailingPathDelimiter(Home) + 'Documents', [rfReplaceAll, rfIgnoreCase]);
  Result := StringReplace(Result, '<winAppData>', GetEnvironmentVariable('APPDATA'), [rfReplaceAll, rfIgnoreCase]);
  Result := StringReplace(Result, '<winLocalAppData>', GetEnvironmentVariable('LOCALAPPDATA'), [rfReplaceAll, rfIgnoreCase]);
  Result := StringReplace(Result, '<winCommonAppData>', GetEnvironmentVariable('PROGRAMDATA'), [rfReplaceAll, rfIgnoreCase]);
  Result := StringReplace(Result, '<winCommonDocuments>', IncludeTrailingPathDelimiter(GetEnvironmentVariable('PUBLIC')) + 'Documents', [rfReplaceAll, rfIgnoreCase]);
  Result := StringReplace(Result, '<winPublic>', GetEnvironmentVariable('PUBLIC'), [rfReplaceAll, rfIgnoreCase]);
  Result := StringReplace(Result, '<winDesktop>', IncludeTrailingPathDelimiter(Home) + 'Desktop', [rfReplaceAll, rfIgnoreCase]);
  Result := StringReplace(Result, '<winLocalAppDataLow>', IncludeTrailingPathDelimiter(Home) + 'AppData\LocalLow', [rfReplaceAll, rfIgnoreCase]);
end;

function TMainForm.GetCurrentDevicePath(Paths: TJSONObject): string;
begin
  Result := Paths.Get(FCurrentDeviceId, '');
  if (Result = '') and (Paths.Count > 0) then
    Result := Paths.Items[0].AsString;
end;

function TMainForm.GetCurrentGamePath(Game: TJSONObject): string;
var
  GamePaths: TJSONObject;
begin
  GamePaths := JsonObject(Game, 'game_paths');
  Result := GamePaths.Get(FCurrentDeviceId, '');
  if (Result = '') and (GamePaths.Count > 0) then
    Result := GamePaths.Items[0].AsString;
end;

function TMainForm.GetGameName(Game: TJSONObject): string;
begin
  Result := Game.Get('name', '(unnamed)');
end;

function TMainForm.ResolveBackupRoot: string;
var
  BackupPath: string;
begin
  BackupPath := ResolvePathTokens(FConfig.Get('backup_path', './save_data'));
  if IsAbsolutePath(BackupPath) then
    Result := ExpandFileName(BackupPath)
  else
    Result := ExpandFileName(IncludeTrailingPathDelimiter(ExtractFileDir(FConfigPath)) + BackupPath);
end;

function TMainForm.FindCloudToolPath: string;
var
  Candidate: string;
begin
  Candidate := IncludeTrailingPathDelimiter(ExtractFileDir(ParamStr(0))) + 'GameSaveManager.CloudTool.exe';
  if FileExists(Candidate) then
    Exit(Candidate);

  Candidate := ExpandFileName(IncludeTrailingPathDelimiter(ExtractFileDir(ParamStr(0))) +
    '..\EflayGameSaveManager.CloudTool\bin\Release\net10.0\win-x64\GameSaveManager.CloudTool.exe');
  if FileExists(Candidate) then
    Exit(Candidate);

  Candidate := ExpandFileName('src\EflayGameSaveManager.CloudTool\bin\Release\net10.0\win-x64\GameSaveManager.CloudTool.exe');
  if FileExists(Candidate) then
    Exit(Candidate);

  raise Exception.Create('GameSaveManager.CloudTool.exe not found. Publish or copy it next to GameSaveManagerLite.exe.');
end;

function TMainForm.ReadLiteConfigSevenZipDir: string;
var
  LiteConfigPath: string;
  JsonText: TStringList;
  Parser: TJSONParser;
  Root: TJSONObject;
  Settings: TJSONObject;
begin
  Result := '';
  LiteConfigPath := IncludeTrailingPathDelimiter(ExtractFileDir(FConfigPath)) + LiteConfigFileName;
  if not FileExists(LiteConfigPath) then
    Exit;

  JsonText := TStringList.Create;
  try
    JsonText.LoadFromFile(LiteConfigPath);
    Parser := TJSONParser.Create(JsonText.Text);
    try
      Root := Parser.Parse as TJSONObject;
      try
        Settings := Root.Objects['settings'];
        if Settings <> nil then
          Result := Trim(Settings.Get('seven_zip_dir', ''));
      finally
        Root.Free;
      end;
    finally
      Parser.Free;
    end;
  finally
    JsonText.Free;
  end;
end;

function TMainForm.Resolve7ZipExecutablePath: string;
var
  SevenZipDir: string;
  Candidate: string;
  DllPath: string;
begin
  SevenZipDir := ReadLiteConfigSevenZipDir;
  if SevenZipDir <> '' then
  begin
    SevenZipDir := ResolvePathTokens(SevenZipDir);
    if not IsAbsolutePath(SevenZipDir) then
      SevenZipDir := ExpandFileName(IncludeTrailingPathDelimiter(ExtractFileDir(FConfigPath)) + SevenZipDir);
    Candidate := IncludeTrailingPathDelimiter(SevenZipDir) + '7zz.exe';
    if FileExists(Candidate) then
      Exit(Candidate);
    Candidate := IncludeTrailingPathDelimiter(SevenZipDir) + '7za.exe';
    if FileExists(Candidate) then
      Exit(Candidate);
    Candidate := IncludeTrailingPathDelimiter(SevenZipDir) + '7z.exe';
    if FileExists(Candidate) then
    begin
      DllPath := IncludeTrailingPathDelimiter(SevenZipDir) + '7z.dll';
      if not FileExists(DllPath) then
        raise Exception.Create('7z.exe found but 7z.dll is missing in: ' + SevenZipDir + '. Use 7zz.exe or copy matching 7z.dll.');
      Exit(Candidate);
    end;
    raise Exception.Create('7z executable not found in configured seven_zip_dir: ' + SevenZipDir + '. Expected 7zz.exe, 7za.exe or 7z.exe.');
  end;

  Candidate := IncludeTrailingPathDelimiter(ExtractFileDir(ParamStr(0))) + '7zz.exe';
  if FileExists(Candidate) then
    Exit(Candidate);
  Candidate := IncludeTrailingPathDelimiter(ExtractFileDir(ParamStr(0))) + '7za.exe';
  if FileExists(Candidate) then
    Exit(Candidate);

  Candidate := IncludeTrailingPathDelimiter(ExtractFileDir(ParamStr(0))) + '7z.exe';
  if FileExists(Candidate) then
  begin
    DllPath := IncludeTrailingPathDelimiter(ExtractFileDir(ParamStr(0))) + '7z.dll';
    if not FileExists(DllPath) then
      raise Exception.Create('7z.exe found but 7z.dll is missing next to GameSaveManagerLite.exe. Use 7zz.exe or copy matching 7z.dll.');
    Exit(Candidate);
  end;

  raise Exception.Create('7z executable not found. Put 7zz.exe or 7za.exe (recommended), or 7z.exe+7z.dll next to GameSaveManagerLite.exe, or set settings.seven_zip_dir in ' + LiteConfigFileName + '.');
end;

procedure TMainForm.ExtractArchiveWith7Zip(const ArchivePath, ExtractRoot: string);
var
  Proc: TProcess;
  Output: TStringList;
  SevenZipPath: string;
  OutputText: string;
  ArchiveSize: Int64;
begin
  if not FileExists(ArchivePath) then
    raise Exception.Create('Downloaded archive file not found: ' + ArchivePath);

  with TFileStream.Create(ArchivePath, fmOpenRead or fmShareDenyNone) do
  try
    ArchiveSize := Size;
  finally
    Free;
  end;

  if ArchiveSize <= 0 then
    raise Exception.Create('Downloaded archive file is empty: ' + ArchivePath);

  SevenZipPath := Resolve7ZipExecutablePath;

  Proc := TProcess.Create(nil);
  Output := TStringList.Create;
  try
    Proc.Executable := SevenZipPath;
    Proc.Parameters.Add('x');
    Proc.Parameters.Add('-y');
    Proc.Parameters.Add('-aoa');
    Proc.Parameters.Add('-bb1');
    Proc.Parameters.Add('-o' + ExtractRoot);
    Proc.Parameters.Add(ArchivePath);
    Proc.CurrentDirectory := ExtractFileDir(ArchivePath);
    Proc.Options := [poUsePipes, poStderrToOutPut, poWaitOnExit];
    Proc.Execute;
    Output.LoadFromStream(Proc.Output);
    OutputText := Trim(Output.Text);

    if Proc.ExitStatus <> 0 then
      raise Exception.Create('7z extract failed. exit=' + IntToStr(Proc.ExitStatus) +
        ', archive=' + ArchivePath + ', size=' + IntToStr(ArchiveSize) + '. ' + OutputText);
  finally
    Output.Free;
    Proc.Free;
  end;
end;

function TMainForm.SanitizeSegment(const Value: string): string;
var
  I: Integer;
  C: Char;
begin
  Result := Trim(Value);
  for I := 1 to Length(Result) do
  begin
    C := Result[I];
    if C in ['\', '/', ':', '*', '?', '"', '<', '>', '|'] then
      Result[I] := '_';
  end;

  if Result = '' then
    Result := '_';
end;

function TMainForm.IsAbsolutePath(const Value: string): Boolean;
begin
  Result := (ExtractFileDrive(Value) <> '') or
    ((Length(Value) > 0) and (Value[1] in ['\', '/']));
end;

function TMainForm.JsonArray(const Obj: TJSONObject; const AName: string): TJSONArray;
begin
  Result := Obj.Arrays[AName];
  if Result = nil then
    raise Exception.Create('Missing JSON array: ' + AName);
end;

function TMainForm.JsonObject(const Obj: TJSONObject; const AName: string): TJSONObject;
begin
  Result := Obj.Objects[AName];
  if Result = nil then
    raise Exception.Create('Missing JSON object: ' + AName);
end;

function TMainForm.SelectedSavePath: string;
begin
  Result := '';
  if FSaveGrid.Row > 0 then
    Result := FSaveGrid.Cells[2, FSaveGrid.Row]
  else if FSaveGrid.RowCount > 1 then
    Result := FSaveGrid.Cells[2, 1];
end;

function TMainForm.SelectedSaveUnitType: string;
begin
  Result := '';
  if FSaveGrid.Row > 0 then
    Result := FSaveGrid.Cells[1, FSaveGrid.Row]
  else if FSaveGrid.RowCount > 1 then
    Result := FSaveGrid.Cells[1, 1];
end;

procedure TMainForm.OpenFolderForPath(const PathValue: string);
var
  Target: string;
  Proc: TProcess;
begin
  Target := Trim(PathValue);
  if Target = '' then
  begin
    SetStatus('Path is empty.');
    Exit;
  end;

  if FileExists(Target) then
    Target := ExtractFileDir(Target);

  if not DirectoryExists(Target) then
  begin
    SetStatus('Directory not found: ' + Target);
    Exit;
  end;

  Proc := TProcess.Create(nil);
  try
    Proc.Executable := 'explorer.exe';
    Proc.Parameters.Add(Target);
    Proc.Options := [];
    Proc.Execute;
  finally
    Proc.Free;
  end;
end;

procedure TMainForm.CopyPathToBackup(const SourcePath, DestinationPath: string);
begin
  if FileExists(SourcePath) then
  begin
    ForceDirectories(DestinationPath);
    CopyFileToBackup(SourcePath, IncludeTrailingPathDelimiter(DestinationPath) + ExtractFileName(SourcePath));
  end
  else if DirectoryExists(SourcePath) then
    CopyDirectoryToBackup(SourcePath, DestinationPath)
  else
    SetStatus('Save path not found, skipped: ' + SourcePath);
end;

procedure TMainForm.CopyDirectoryToBackup(const SourceDir, DestinationDir: string);
var
  Search: TSearchRec;
  SourceItem: string;
  DestinationItem: string;
begin
  ForceDirectories(DestinationDir);
  if FindFirst(IncludeTrailingPathDelimiter(SourceDir) + '*', faAnyFile, Search) <> 0 then
    Exit;

  try
    repeat
      if (Search.Name = '.') or (Search.Name = '..') then
        Continue;

      SourceItem := IncludeTrailingPathDelimiter(SourceDir) + Search.Name;
      DestinationItem := IncludeTrailingPathDelimiter(DestinationDir) + Search.Name;
      if (Search.Attr and faDirectory) <> 0 then
        CopyDirectoryToBackup(SourceItem, DestinationItem)
      else
        CopyFileToBackup(SourceItem, DestinationItem);
    until FindNext(Search) <> 0;
  finally
    FindClose(Search);
  end;
end;

procedure TMainForm.CopyFileToBackup(const SourceFile, DestinationFile: string);
var
  SourceStream: TFileStream;
  DestinationStream: TFileStream;
begin
  ForceDirectories(ExtractFileDir(DestinationFile));
  SourceStream := TFileStream.Create(SourceFile, fmOpenRead or fmShareDenyWrite);
  try
    DestinationStream := TFileStream.Create(DestinationFile, fmCreate);
    try
      DestinationStream.CopyFrom(SourceStream, 0);
    finally
      DestinationStream.Free;
    end;
  finally
    SourceStream.Free;
  end;
end;

function TMainForm.RunRegCommand(const Args: array of string): Boolean;
var
  Proc: TProcess;
  I: Integer;
begin
  Result := False;
  Proc := TProcess.Create(nil);
  try
    Proc.Executable := 'reg.exe';
    for I := Low(Args) to High(Args) do
      Proc.Parameters.Add(Args[I]);
    Proc.Options := [poWaitOnExit];
    Proc.Execute;
    Result := Proc.ExitStatus = 0;
  finally
    Proc.Free;
  end;
end;

function TMainForm.RegistryKeyExists(const RegistryPath: string): Boolean;
begin
  Result := (Trim(RegistryPath) <> '') and RunRegCommand(['query', RegistryPath]);
end;

function TMainForm.ExportRegistryKey(const RegistryPath, DestinationFile: string): Boolean;
begin
  Result := False;
  if Trim(RegistryPath) = '' then
    Exit;
  ForceDirectories(ExtractFileDir(DestinationFile));
  if FileExists(DestinationFile) then
    DeleteFile(DestinationFile);
  Result := RunRegCommand(['export', RegistryPath, DestinationFile, '/y']) and FileExists(DestinationFile);
end;

function TMainForm.ImportRegistryFile(const RegistryFile: string): Boolean;
begin
  Result := FileExists(RegistryFile) and RunRegCommand(['import', RegistryFile]);
end;

function TMainForm.DeleteRegistryKey(const RegistryPath: string): Boolean;
begin
  Result := False;
  if Trim(RegistryPath) = '' then
    Exit;
  if not RegistryKeyExists(RegistryPath) then
    Exit;
  Result := RunRegCommand(['delete', RegistryPath, '/f']);
end;

procedure TMainForm.RunPascalCloudStatus;
var
  Settings: TJSONObject;
  CloudSettings: TJSONObject;
  Backend: TS3Backend;
  ObjectKey: string;
  Content: string;
  StatusCode: Integer;
  Parser: TJSONParser;
  BackupsJson: TJSONObject;
  Backups: TJSONArray;
  DeviceHeads: TJSONObject;
  CurrentHead: string;
  HeadDeviceId: string;
  I: Integer;
  HeadIndex: Integer;
  Backup: TJSONObject;
  Line: string;
begin
  if FSelectedGame = nil then
  begin
    SetStatus('No game selected.');
    Exit;
  end;

  try
    Settings := JsonObject(FConfig, 'settings');
    CloudSettings := JsonObject(Settings, 'cloud_settings');
    Backend := ReadS3Backend(CloudSettings);
    ObjectKey := BuildGameBackupsKey(CloudSettings, GetGameName(FSelectedGame));
    SetStatus('Reading cloud status with Pascal S3 client...');

    if not TPascalS3Client.TryDownloadUtf8String(Backend, ObjectKey, Content, StatusCode) then
    begin
      FInfo.Lines.Clear;
      if StatusCode = 404 then
      begin
        FInfo.Lines.Add('Cloud backups not found.');
        FInfo.Lines.Add('Object: ' + ObjectKey);
      end
      else
      begin
        FInfo.Lines.Add('Cloud status request failed.');
        FInfo.Lines.Add('HTTP status: ' + IntToStr(StatusCode));
        FInfo.Lines.Add(Content);
      end;
      SetStatus('Pascal cloud status failed.');
      Exit;
    end;

    Parser := TJSONParser.Create(Content);
    try
      BackupsJson := Parser.Parse as TJSONObject;
      try
        Backups := nil;
        BackupsJson.Find('backups', Backups);
        DeviceHeads := nil;
        BackupsJson.Find('device_heads', DeviceHeads);
        CurrentHead := '';
        if DeviceHeads <> nil then
          CurrentHead := DeviceHeads.Get(FCurrentDeviceId, '');

        FInfo.Lines.BeginUpdate;
        try
          FInfo.Lines.Clear;
          FInfo.Lines.Add('Pascal cloud status');
          FInfo.Lines.Add('Game: ' + GetGameName(FSelectedGame));
          FInfo.Lines.Add('Object: ' + ObjectKey);
          FInfo.Lines.Add('Current device: ' + FCurrentDeviceName + ' [' + FCurrentDeviceId + ']');
          if CurrentHead = '' then
            FInfo.Lines.Add('Current head: -')
          else
            FInfo.Lines.Add('Current head: ' + CurrentHead);
          if Backups = nil then
            FInfo.Lines.Add('Backup count: 0')
          else
          begin
            FInfo.Lines.Add('Backup count: ' + IntToStr(Backups.Count));
            for I := 0 to Backups.Count - 1 do
            begin
              Backup := Backups.Objects[I];
              Line := Backup.Get('date', '-') + ' | ' +
                IntToStr(Backup.Get('size', 0)) + ' B | device: ' +
                Backup.Get('device_id', '-');
              if (CurrentHead <> '') and SameText(Backup.Get('date', ''), CurrentHead) then
                Line += ' | current device head';
              if DeviceHeads <> nil then
              begin
                for HeadIndex := 0 to DeviceHeads.Count - 1 do
                begin
                  HeadDeviceId := DeviceHeads.Names[HeadIndex];
                  if SameText(Backup.Get('device_id', ''), HeadDeviceId) and
                    (Backup.Get('date', '') = DeviceHeads.Get(HeadDeviceId, '')) then
                    Line += ' | device head';
                end;
              end;
              FInfo.Lines.Add(Line);
            end;
          end;
        finally
          FInfo.Lines.EndUpdate;
        end;

        SetStatus('Pascal cloud status completed.');
      finally
        BackupsJson.Free;
      end;
    finally
      Parser.Free;
    end;
  except
    on E: Exception do
    begin
      FInfo.Lines.Clear;
      FInfo.Lines.Add(E.Message);
      SetStatus('Pascal cloud status failed.');
    end;
  end;
end;

function BackupString(const Backup: TJSONObject; const JsonName, DefaultValue: string): string;
begin
  Result := Backup.Get(JsonName, DefaultValue);
end;

function NormalizeCloudPath(const Value: string): string;
var
  Parts: TStringArray;
  Part: string;
  TrimmedPart: string;
begin
  Result := '';
  Parts := StringReplace(Value, '/', '\', [rfReplaceAll]).Split('\');
  for Part in Parts do
  begin
    TrimmedPart := Trim(Part);
    if (TrimmedPart = '') or (TrimmedPart = '.') then
      Continue;
    if Result <> '' then
      Result += '/';
    Result += TrimmedPart;
  end;
end;

function NormalizeLegacyArchivePath(const OriginalPath: string; const CloudSettings: TJSONObject): string;
var
  Normalized: string;
  LowerNormalized: string;
  MarkerPos: SizeInt;
  RootPath: string;
  ForwardPath: string;
const
  SaveDataMarker = '\save_data\';
  SaveDataMarkerForward = '/save_data/';
  SaveDataPrefix = 'save_data\';
begin
  Normalized := StringReplace(Trim(OriginalPath), '/', '\', [rfReplaceAll]);
  if Copy(Normalized, 1, 2) = './' then
    Delete(Normalized, 1, 2)
  else if Copy(Normalized, 1, 2) = '.\' then
    Delete(Normalized, 1, 2);

  LowerNormalized := LowerCase(Normalized);
  MarkerPos := Pos(SaveDataMarker, LowerNormalized);
  if MarkerPos = 0 then
  begin
    ForwardPath := StringReplace(LowerNormalized, '\', '/', [rfReplaceAll]);
    MarkerPos := Pos(SaveDataMarkerForward, ForwardPath);
    if MarkerPos > 0 then
      Normalized := SaveDataPrefix + Copy(StringReplace(Trim(OriginalPath), '\', '/', [rfReplaceAll]), MarkerPos + Length(SaveDataMarkerForward), MaxInt);
  end;
  if MarkerPos > 0 then
    Normalized := SaveDataPrefix + Copy(Normalized, MarkerPos + Length(SaveDataMarker), MaxInt);

  Normalized := StringReplace(NormalizeCloudPath(Normalized), '/', '\', [rfReplaceAll]);

  RootPath := StringReplace(Trim(CloudSettings.Get('root_path', '')), '/', '\', [rfReplaceAll]);
  while (Length(RootPath) > 0) and (RootPath[1] = '\') do
    Delete(RootPath, 1, 1);
  while (Length(RootPath) > 0) and (RootPath[Length(RootPath)] = '\') do
    Delete(RootPath, Length(RootPath), 1);

  if SameText(Copy(Normalized, 1, Length(SaveDataPrefix)), SaveDataPrefix) then
  begin
    if RootPath <> '' then
      Normalized := RootPath + '\' + Normalized;
  end;

  Result := Normalized;
end;

function CombineCloudKey(const Segments: array of string): string;
var
  Segment: string;
  Normalized: string;
begin
  Result := '';
  for Segment in Segments do
  begin
    Normalized := StringReplace(Trim(Segment), '\', '/', [rfReplaceAll]);
    while (Length(Normalized) > 0) and (Normalized[1] = '/') do
      Delete(Normalized, 1, 1);
    while (Length(Normalized) > 0) and (Normalized[Length(Normalized)] = '/') do
      Delete(Normalized, Length(Normalized), 1);
    if Normalized = '' then
      Continue;
    if Result <> '' then
      Result += '/';
    Result += Normalized;
  end;
end;

function RelativeArchiveName(const RootDir, FilePath: string): string;
var
  Root: string;
begin
  Root := IncludeTrailingPathDelimiter(ExpandFileName(RootDir));
  Result := ExpandFileName(FilePath);
  if Pos(Root, Result) = 1 then
    Delete(Result, 1, Length(Root));
  Result := StringReplace(Result, '\', '/', [rfReplaceAll]);
end;

procedure EnsureJsonArray(const Obj: TJSONObject; const AName: string; out Value: TJSONArray);
begin
  Value := nil;
  Obj.Find(AName, Value);
  if Value = nil then
  begin
    Value := TJSONArray.Create;
    Obj.Add(AName, Value);
  end;
end;

procedure EnsureJsonObject(const Obj: TJSONObject; const AName: string; out Value: TJSONObject);
begin
  Value := nil;
  Obj.Find(AName, Value);
  if Value = nil then
  begin
    Value := TJSONObject.Create;
    Obj.Add(AName, Value);
  end;
end;

function StringEndsWithText(const Value, Suffix: string): Boolean;
begin
  Result := (Length(Value) >= Length(Suffix)) and
    SameText(Copy(Value, Length(Value) - Length(Suffix) + 1, Length(Suffix)), Suffix);
end;

function IsDirectBackupZipObject(const ObjectKey, RootKey: string): Boolean;
var
  NormalizedKey: string;
  NormalizedRoot: string;
  RelativePath: string;
begin
  NormalizedKey := StringReplace(ObjectKey, '\', '/', [rfReplaceAll]);
  NormalizedRoot := CombineCloudKey([RootKey]) + '/';
  Result := False;
  if (not SameText(Copy(NormalizedKey, 1, Length(NormalizedRoot)), NormalizedRoot)) or
     (not StringEndsWithText(NormalizedKey, '.zip')) then
    Exit;

  RelativePath := Copy(NormalizedKey, Length(NormalizedRoot) + 1, MaxInt);
  Result := (RelativePath <> '') and (Pos('/', RelativePath) = 0) and
    (ChangeFileExt(ExtractFileName(RelativePath), '') <> '');
end;

function BackupDateFromObjectKey(const ObjectKey: string): string;
var
  NormalizedKey: string;
  SlashPos: SizeInt;
  FileName: string;
begin
  NormalizedKey := StringReplace(ObjectKey, '\', '/', [rfReplaceAll]);
  SlashPos := RPos('/', NormalizedKey);
  if SlashPos > 0 then
    FileName := Copy(NormalizedKey, SlashPos + 1, MaxInt)
  else
    FileName := NormalizedKey;
  Result := ChangeFileExt(FileName, '');
end;

function BuildBackupRelativePath(const GameName, BackupDate: string): string;
begin
  Result := '.\save_data\' + GameName + '\' + BackupDate + '.zip';
end;

function FindBackupByDate(const Backups: TJSONArray; const BackupDate: string): TJSONObject;
var
  I: Integer;
  Candidate: TJSONObject;
begin
  Result := nil;
  if Backups = nil then
    Exit;

  for I := 0 to Backups.Count - 1 do
  begin
    Candidate := Backups.Objects[I];
    if Candidate.Get('date', '') = BackupDate then
      Exit(Candidate);
  end;
end;

procedure TMainForm.RunPascalCloudRebuildManifest;
var
  Settings: TJSONObject;
  CloudSettings: TJSONObject;
  Backend: TS3Backend;
  GameName: string;
  RootKey: string;
  ManifestKey: string;
  ManifestContent: string;
  ManifestJson: string;
  ErrorContent: string;
  StatusCode: Integer;
  Objects: TS3ObjectInfoArray;
  Parser: TJSONParser;
  OldManifest: TJSONObject;
  OldBackups: TJSONArray;
  OldDeviceHeads: TJSONObject;
  Manifest: TJSONObject;
  Backups: TJSONArray;
  DeviceHeads: TJSONObject;
  Entry: TJSONObject;
  Existing: TJSONObject;
  BackupDate: string;
  DeviceId: string;
  I: Integer;
  J: Integer;
  ZipCount: Integer;
  PreservedCount: Integer;
  BestDate: string;
  ExistingHeadDate: string;
begin
  if FSelectedGame = nil then
  begin
    SetStatus('No game selected.');
    Exit;
  end;

  OldManifest := nil;
  try
    Settings := JsonObject(FConfig, 'settings');
    CloudSettings := JsonObject(Settings, 'cloud_settings');
    Backend := ReadS3Backend(CloudSettings);
    GameName := GetGameName(FSelectedGame);
    RootKey := CombineCloudKey([CloudSettings.Get('root_path', ''), 'save_data', GameName]);
    ManifestKey := CombineCloudKey([RootKey, 'Backups.json']);

    SetStatus('Listing cloud backup zip files with Pascal S3 client...');
    if not TPascalS3Client.TryListObjects(Backend, RootKey + '/', Objects, ErrorContent, StatusCode) then
      raise Exception.Create('Cloud object list failed: HTTP ' + IntToStr(StatusCode) + ' ' + ErrorContent);

    if TPascalS3Client.TryDownloadUtf8String(Backend, ManifestKey, ManifestContent, StatusCode) then
    begin
      Parser := TJSONParser.Create(ManifestContent);
      try
        OldManifest := Parser.Parse as TJSONObject;
      finally
        Parser.Free;
      end;
    end
    else if StatusCode <> 404 then
      raise Exception.Create('Cloud manifest request failed: HTTP ' + IntToStr(StatusCode) + ' ' + ManifestContent);

    OldBackups := nil;
    OldDeviceHeads := nil;
    if OldManifest <> nil then
    begin
      OldManifest.Find('backups', OldBackups);
      OldManifest.Find('device_heads', OldDeviceHeads);
    end;

    Manifest := TJSONObject.Create;
    try
      Manifest.Add('name', GameName);
      if OldManifest <> nil then
        Manifest.Add('sync_version', OldManifest.Get('sync_version', 0))
      else
        Manifest.Add('sync_version', 0);
      Backups := TJSONArray.Create;
      DeviceHeads := TJSONObject.Create;
      Manifest.Add('backups', Backups);
      Manifest.Add('device_heads', DeviceHeads);

      ZipCount := 0;
      PreservedCount := 0;
      for I := 0 to Length(Objects) - 1 do
      begin
        if not IsDirectBackupZipObject(Objects[I].Key, RootKey) then
          Continue;

        BackupDate := BackupDateFromObjectKey(Objects[I].Key);
        Existing := FindBackupByDate(OldBackups, BackupDate);
        Entry := TJSONObject.Create;
        Entry.Add('date', BackupDate);
        if Existing <> nil then
        begin
          Entry.Add('describe', Existing.Get('describe', ''));
          Entry.Add('path', Existing.Get('path', BuildBackupRelativePath(GameName, BackupDate)));
          if Existing.Get('parent', '') <> '' then
            Entry.Add('parent', Existing.Get('parent', ''));
          DeviceId := Existing.Get('device_id', FCurrentDeviceId);
          Inc(PreservedCount);
        end
        else
        begin
          Entry.Add('describe', 'Rebuilt from cloud object list');
          Entry.Add('path', BuildBackupRelativePath(GameName, BackupDate));
          DeviceId := FCurrentDeviceId;
        end;
        Entry.Add('size', Objects[I].Size);
        Entry.Add('device_id', DeviceId);
        Backups.Add(Entry);
        Inc(ZipCount);
      end;

      if ZipCount = 0 then
        raise Exception.Create('No cloud backup zip files found for ' + GameName + '.');

      if OldDeviceHeads <> nil then
      begin
        for I := 0 to OldDeviceHeads.Count - 1 do
        begin
          DeviceId := OldDeviceHeads.Names[I];
          ExistingHeadDate := OldDeviceHeads.Get(DeviceId, '');
          if FindBackupByDate(Backups, ExistingHeadDate) <> nil then
            DeviceHeads.Strings[DeviceId] := ExistingHeadDate;
        end;
      end;

      for I := 0 to Backups.Count - 1 do
      begin
        Entry := Backups.Objects[I];
        DeviceId := Entry.Get('device_id', '');
        if (DeviceId = '') or (DeviceHeads.Get(DeviceId, '') <> '') then
          Continue;

        BestDate := Entry.Get('date', '');
        for J := I + 1 to Backups.Count - 1 do
        begin
          if SameText(Backups.Objects[J].Get('device_id', ''), DeviceId) and
             (CompareText(Backups.Objects[J].Get('date', ''), BestDate) > 0) then
            BestDate := Backups.Objects[J].Get('date', '');
        end;
        DeviceHeads.Strings[DeviceId] := BestDate;
      end;

      ManifestJson := Manifest.FormatJSON;
    finally
      Manifest.Free;
    end;

    SetStatus('Uploading rebuilt cloud manifest with Pascal S3 client...');
    if not TPascalS3Client.TryUploadUtf8String(Backend, ManifestKey, ManifestJson, ErrorContent, StatusCode) then
      raise Exception.Create('Cloud manifest upload failed: HTTP ' + IntToStr(StatusCode) + ' ' + ErrorContent);

    FInfo.Lines.Clear;
    FInfo.Lines.Add('Pascal cloud manifest rebuild completed.');
    FInfo.Lines.Add('Game: ' + GameName);
    FInfo.Lines.Add('Object: ' + ManifestKey);
    FInfo.Lines.Add('Zip files: ' + IntToStr(ZipCount));
    FInfo.Lines.Add('Preserved old entries: ' + IntToStr(PreservedCount));
    SetStatus('Pascal cloud manifest rebuild completed.');
  except
    on E: Exception do
    begin
      FInfo.Lines.Clear;
      FInfo.Lines.Add(E.Message);
      SetStatus('Pascal cloud manifest rebuild failed.');
    end;
  end;

  OldManifest.Free;
end;

procedure TMainForm.RunPascalCloudUpload;
var
  Settings: TJSONObject;
  CloudSettings: TJSONObject;
  Backend: TS3Backend;
  GameName: string;
  RootKey: string;
  ArchiveKey: string;
  ManifestKey: string;
  Timestamp: string;
  WorkRoot: string;
  ArchivePath: string;
  ManifestContent: string;
  ManifestJson: string;
  ErrorContent: string;
  StatusCode: Integer;
  Parser: TJSONParser;
  Manifest: TJSONObject;
  Backups: TJSONArray;
  DeviceHeads: TJSONObject;
  Entry: TJSONObject;
  Guid: TGUID;
  UploadedBytes: Int64;
  ArchiveSize: Int64;
begin
  if FSelectedGame = nil then
  begin
    SetStatus('No game selected.');
    Exit;
  end;

  try
    Settings := JsonObject(FConfig, 'settings');
    CloudSettings := JsonObject(Settings, 'cloud_settings');
    Backend := ReadS3Backend(CloudSettings);
    GameName := GetGameName(FSelectedGame);
    RootKey := CombineCloudKey([CloudSettings.Get('root_path', ''), 'save_data', GameName]);
    ManifestKey := CombineCloudKey([RootKey, 'Backups.json']);
    Timestamp := FormatDateTime('yyyy-mm-dd_hh-nn-ss', Now);
    ArchiveKey := CombineCloudKey([RootKey, Timestamp + '.zip']);

    CreateGUID(Guid);
    WorkRoot := IncludeTrailingPathDelimiter(GetTempDir(False)) + 'EflayGameSaveManager' +
      DirectorySeparator + 'pascal-upload' + DirectorySeparator +
      StringReplace(StringReplace(LowerCase(GUIDToString(Guid)), '{', '', [rfReplaceAll]), '}', '', [rfReplaceAll]);
    ForceDirectories(WorkRoot);

    try
      SetStatus('Creating current save archive with Pascal zipper...');
      ArchivePath := CreateCurrentDeviceArchive(WorkRoot);

      SetStatus('Uploading cloud archive with Pascal S3 client...');
      if not TPascalS3Client.TryUploadFile(Backend, ArchiveKey, ArchivePath, ErrorContent, StatusCode) then
        raise Exception.Create('Cloud archive upload failed: HTTP ' + IntToStr(StatusCode) + ' ' + ErrorContent);

      if TPascalS3Client.TryDownloadUtf8String(Backend, ManifestKey, ManifestContent, StatusCode) then
      begin
        Parser := TJSONParser.Create(ManifestContent);
        try
          Manifest := Parser.Parse as TJSONObject;
        finally
          Parser.Free;
        end;
      end
      else if StatusCode <> 404 then
        raise Exception.Create('Cloud manifest request failed: HTTP ' + IntToStr(StatusCode) + ' ' + ManifestContent)
      else
      begin
        Manifest := TJSONObject.Create;
        Manifest.Add('name', GameName);
        Manifest.Add('sync_version', 0);
      end;

      try
        EnsureJsonArray(Manifest, 'backups', Backups);
        EnsureJsonObject(Manifest, 'device_heads', DeviceHeads);
        with TFileStream.Create(ArchivePath, fmOpenRead) do
        try
          ArchiveSize := Size;
        finally
          Free;
        end;

        Entry := TJSONObject.Create;
        Entry.Add('date', Timestamp);
        Entry.Add('describe', '');
        Entry.Add('path', '.\save_data\' + GameName + '\' + Timestamp + '.zip');
        Entry.Add('size', ArchiveSize);
        Entry.Add('device_id', FCurrentDeviceId);
        Backups.Add(Entry);
        DeviceHeads.Strings[FCurrentDeviceId] := Timestamp;

        ManifestJson := Manifest.FormatJSON;
      finally
        Manifest.Free;
      end;

      SetStatus('Uploading cloud manifest with Pascal S3 client...');
      if not TPascalS3Client.TryUploadUtf8String(Backend, ManifestKey, ManifestJson, ErrorContent, StatusCode) then
        raise Exception.Create('Cloud manifest upload failed: HTTP ' + IntToStr(StatusCode) + ' ' + ErrorContent);

      UploadedBytes := Length(UTF8String(ManifestJson));
      with TFileStream.Create(ArchivePath, fmOpenRead) do
      try
        Inc(UploadedBytes, Size);
      finally
        Free;
      end;

      FInfo.Lines.Clear;
      FInfo.Lines.Add('Pascal cloud upload completed.');
      FInfo.Lines.Add('Game: ' + GameName);
      FInfo.Lines.Add('Cloud root: ' + RootKey);
      FInfo.Lines.Add('Archive: ' + ArchiveKey);
      FInfo.Lines.Add('Objects: 2');
      FInfo.Lines.Add('Bytes: ' + IntToStr(UploadedBytes));
      SetStatus('Pascal cloud upload completed.');
    finally
      if DirectoryExists(WorkRoot) then
        DeletePathRecursive(WorkRoot);
    end;
  except
    on E: Exception do
    begin
      FInfo.Lines.Clear;
      FInfo.Lines.Add(E.Message);
      SetStatus('Pascal cloud upload failed.');
    end;
  end;
end;

function TMainForm.CreateCurrentDeviceArchive(const WorkRoot: string): string;
var
  SavePaths: TJSONArray;
  SaveUnit: TJSONObject;
  Paths: TJSONObject;
  StagingRoot: string;
  StagedPath: string;
  SourcePath: string;
  UnitType: string;
  Zipper: TZipper;
  I: Integer;
begin
  StagingRoot := IncludeTrailingPathDelimiter(WorkRoot) + 'content';
  ForceDirectories(StagingRoot);

  SavePaths := JsonArray(FSelectedGame, 'save_paths');
  for I := 0 to SavePaths.Count - 1 do
  begin
    SaveUnit := SavePaths.Objects[I];
    Paths := JsonObject(SaveUnit, 'paths');
    SourcePath := ResolvePathTokens(GetCurrentDevicePath(Paths));
    UnitType := SaveUnit.Get('unit_type', '');
    if SameText(UnitType, 'WinRegistry') then
    begin
      if (SourcePath = '') or (not RegistryKeyExists(SourcePath)) then
        Continue;
    end
    else if (SourcePath = '') or ((not FileExists(SourcePath)) and (not DirectoryExists(SourcePath))) then
      Continue;
    StagedPath := IncludeTrailingPathDelimiter(StagingRoot) + IntToStr(SaveUnit.Get('id', I));
    StageSaveUnit(SourcePath, StagedPath, UnitType);
  end;

  Result := IncludeTrailingPathDelimiter(WorkRoot) + 'current-device-save.zip';
  if FileExists(Result) then
    DeleteFile(Result);

  Zipper := TZipper.Create;
  try
    Zipper.FileName := Result;
    AddDirectoryToZip(Zipper, StagingRoot, StagingRoot);
    Zipper.ZipAllFiles;
  finally
    Zipper.Free;
  end;
end;

procedure TMainForm.StageSaveUnit(const SourcePath, StagedPath, UnitType: string);
begin
  if SameText(UnitType, 'WinRegistry') then
    ExportRegistryKey(SourcePath, IncludeTrailingPathDelimiter(StagedPath) + 'registry.reg')
  else if SameText(UnitType, 'File') then
  begin
    ForceDirectories(StagedPath);
    CopyFileToBackup(SourcePath, IncludeTrailingPathDelimiter(StagedPath) + ExtractFileName(SourcePath));
  end
  else
    CopyDirectoryToBackup(SourcePath, StagedPath);
end;

procedure TMainForm.AddDirectoryToZip(Zipper: TZipper; const RootDir, SourceDir: string);
var
  Search: TSearchRec;
  SourceItem: string;
begin
  if FindFirst(IncludeTrailingPathDelimiter(SourceDir) + '*', faAnyFile, Search) <> 0 then
    Exit;

  try
    repeat
      if (Search.Name = '.') or (Search.Name = '..') then
        Continue;

      SourceItem := IncludeTrailingPathDelimiter(SourceDir) + Search.Name;
      if (Search.Attr and faDirectory) <> 0 then
        AddDirectoryToZip(Zipper, RootDir, SourceItem)
      else
        Zipper.Entries.AddFileEntry(SourceItem, RelativeArchiveName(RootDir, SourceItem));
    until FindNext(Search) <> 0;
  finally
    FindClose(Search);
  end;
end;

procedure TMainForm.RunPascalCloudRestore;
var
  Settings: TJSONObject;
  CloudSettings: TJSONObject;
  Backend: TS3Backend;
  ManifestKey: string;
  ManifestContent: string;
  StatusCode: Integer;
  Parser: TJSONParser;
  Manifest: TJSONObject;
  Backups: TJSONArray;
  DeviceHeads: TJSONObject;
  Backup: TJSONObject;
  ArchiveKey: string;
  WorkRoot: string;
  ExtractRoot: string;
  ArchivePath: string;
  Guid: TGUID;
  ErrorContent: string;
  SavePaths: TJSONArray;
  SaveUnit: TJSONObject;
  Paths: TJSONObject;
  SourceRoot: string;
  TargetPath: string;
  UnitType: string;
  DeleteBeforeApply: Boolean;
  I: Integer;
  RestoredCount: Integer;
begin
  if FSelectedGame = nil then
  begin
    SetStatus('No game selected.');
    Exit;
  end;

  try
    Settings := JsonObject(FConfig, 'settings');
    CloudSettings := JsonObject(Settings, 'cloud_settings');
    Backend := ReadS3Backend(CloudSettings);
    ManifestKey := BuildGameBackupsKey(CloudSettings, GetGameName(FSelectedGame));
    SetStatus('Reading cloud manifest with Pascal S3 client...');

    if not TPascalS3Client.TryDownloadUtf8String(Backend, ManifestKey, ManifestContent, StatusCode) then
      raise Exception.Create('Cloud manifest request failed: HTTP ' + IntToStr(StatusCode) + ' ' + ManifestContent);

    Parser := TJSONParser.Create(ManifestContent);
    try
      Manifest := Parser.Parse as TJSONObject;
      try
        Backups := nil;
        Manifest.Find('backups', Backups);
        DeviceHeads := nil;
        Manifest.Find('device_heads', DeviceHeads);

        Backup := ResolveCurrentCloudBackup(Backups, DeviceHeads);
        if Backup = nil then
          raise Exception.Create('No cloud backup found for this game.');
        ArchiveKey := ResolveArchiveKey(CloudSettings, Backup);
      finally
        Manifest.Free;
      end;
    finally
      Parser.Free;
    end;

    CreateGUID(Guid);
    WorkRoot := IncludeTrailingPathDelimiter(GetTempDir(False)) + 'EflayGameSaveManager' +
      DirectorySeparator + 'pascal-restore' + DirectorySeparator +
      StringReplace(StringReplace(LowerCase(GUIDToString(Guid)), '{', '', [rfReplaceAll]), '}', '', [rfReplaceAll]);
    ExtractRoot := IncludeTrailingPathDelimiter(WorkRoot) + 'content';
    ArchivePath := IncludeTrailingPathDelimiter(WorkRoot) + 'cloud-save.zip';
    ForceDirectories(ExtractRoot);

    try
      SetStatus('Downloading cloud archive with Pascal S3 client...');
      if not TPascalS3Client.TryDownloadFile(Backend, ArchiveKey, ArchivePath, ErrorContent, StatusCode) then
        raise Exception.Create('Cloud archive request failed: HTTP ' + IntToStr(StatusCode) + ' ' + ErrorContent);

      SetStatus('Extracting cloud archive with 7z...');
      ExtractArchiveWith7Zip(ArchivePath, ExtractRoot);

      RestoredCount := 0;
      SavePaths := JsonArray(FSelectedGame, 'save_paths');
      for I := 0 to SavePaths.Count - 1 do
      begin
        SaveUnit := SavePaths.Objects[I];
        SourceRoot := ResolveExtractedUnitRoot(ExtractRoot, SaveUnit.Get('id', I), SavePaths.Count);
        if SourceRoot = '' then
          Continue;

        Paths := JsonObject(SaveUnit, 'paths');
        TargetPath := ResolvePathTokens(GetCurrentDevicePath(Paths));
        if TargetPath = '' then
          Continue;

        UnitType := SaveUnit.Get('unit_type', '');
        DeleteBeforeApply := SaveUnit.Get('delete_before_apply', Settings.Get('default_delete_before_apply', False));
        RestoreExtractedUnit(SourceRoot, TargetPath, UnitType, DeleteBeforeApply);
        Inc(RestoredCount);
      end;

      FInfo.Lines.Clear;
      FInfo.Lines.Add('Pascal cloud restore completed.');
      FInfo.Lines.Add('Game: ' + GetGameName(FSelectedGame));
      FInfo.Lines.Add('Archive: ' + ArchiveKey);
      FInfo.Lines.Add('Restored units: ' + IntToStr(RestoredCount));
      SetStatus('Pascal cloud restore completed.');
    finally
      if DirectoryExists(WorkRoot) then
        DeletePathRecursive(WorkRoot);
    end;
  except
    on E: Exception do
    begin
      FInfo.Lines.Clear;
      FInfo.Lines.Add(E.Message);
      SetStatus('Pascal cloud restore failed.');
    end;
  end;
end;

function TMainForm.ResolveCurrentCloudBackup(const Backups: TJSONArray; const DeviceHeads: TJSONObject): TJSONObject;
var
  I: Integer;
  Backup: TJSONObject;
  BestDate: string;
  BackupDate: string;
begin
  Result := nil;
  if Backups = nil then
    Exit;

  BestDate := '';
  for I := 0 to Backups.Count - 1 do
  begin
    Backup := Backups.Objects[I];
    BackupDate := BackupString(Backup, 'date', '');
    if (Result = nil) or (CompareText(BackupDate, BestDate) > 0) then
    begin
      Result := Backup;
      BestDate := BackupDate;
    end;
  end;
end;

function TMainForm.ResolveArchiveKey(const CloudSettings: TJSONObject; const Backup: TJSONObject): string;
var
  RelativePath: string;
  DateValue: string;
  LowerPath: string;
  MarkerPos: SizeInt;
const
  LegacyPrefix = 'save_data\';
begin
  RelativePath := Trim(BackupString(Backup, 'path', ''));
  if RelativePath <> '' then
  begin
    RelativePath := NormalizeLegacyArchivePath(RelativePath, CloudSettings);
    LowerPath := LowerCase(StringReplace(RelativePath, '\', '/', [rfReplaceAll]));
    if Pos(':/', LowerPath) > 0 then
    begin
      MarkerPos := Pos('/save_data/', LowerPath);
      if MarkerPos > 0 then
        RelativePath := StringReplace(Copy(RelativePath, MarkerPos + 1, MaxInt), '/', '\', [rfReplaceAll]);
    end;

    if SameText(Copy(RelativePath, 1, Length(LegacyPrefix)), LegacyPrefix) then
    begin
      RelativePath := StringReplace(Trim(CloudSettings.Get('root_path', '')), '/', '\', [rfReplaceAll]) + '\' + RelativePath;
    end;

    Exit(NormalizeCloudPath(RelativePath));
  end;

  DateValue := BackupString(Backup, 'date', '');
  Result := BuildGameBackupsKey(CloudSettings, GetGameName(FSelectedGame));
  if Copy(Result, Length(Result) - Length('/Backups.json') + 1, Length('/Backups.json')) = '/Backups.json' then
    Delete(Result, Length(Result) - Length('/Backups.json') + 1, Length('/Backups.json'));
  Result += '/' + DateValue + '.zip';
end;

function TMainForm.ResolveExtractedUnitRoot(const ExtractRoot: string; UnitId, UnitCount: Integer): string;
var
  Search: TSearchRec;
  Candidate: string;
  ChildCount: Integer;
begin
  Result := IncludeTrailingPathDelimiter(ExtractRoot) + IntToStr(UnitId);
  if DirectoryExists(Result) then
    Exit;

  if UnitCount = 1 then
    Exit(ExtractRoot);

  Result := '';
  ChildCount := 0;
  if FindFirst(IncludeTrailingPathDelimiter(ExtractRoot) + '*', faAnyFile, Search) <> 0 then
    Exit;
  try
    repeat
      if (Search.Name = '.') or (Search.Name = '..') then
        Continue;
      Candidate := IncludeTrailingPathDelimiter(ExtractRoot) + Search.Name;
      if (Search.Attr and faDirectory) <> 0 then
      begin
        Inc(ChildCount);
        Result := Candidate;
      end
      else if UnitCount = 1 then
        Exit(ExtractRoot);
    until FindNext(Search) <> 0;
  finally
    FindClose(Search);
  end;

  if ChildCount <> 1 then
    Result := '';
end;

function TMainForm.ResolveFolderContentRoot(const SourceRoot, TargetPath: string): string;
var
  Search: TSearchRec;
  ChildDir: string;
  ChildDirCount: Integer;
  ChildFileCount: Integer;
  TargetName: string;
begin
  Result := SourceRoot;
  ChildDir := '';
  ChildDirCount := 0;
  ChildFileCount := 0;

  if FindFirst(IncludeTrailingPathDelimiter(SourceRoot) + '*', faAnyFile, Search) <> 0 then
    Exit;
  try
    repeat
      if (Search.Name = '.') or (Search.Name = '..') then
        Continue;
      if (Search.Attr and faDirectory) <> 0 then
      begin
        Inc(ChildDirCount);
        ChildDir := IncludeTrailingPathDelimiter(SourceRoot) + Search.Name;
      end
      else
        Inc(ChildFileCount);
    until FindNext(Search) <> 0;
  finally
    FindClose(Search);
  end;

  TargetName := ExtractFileName(ExcludeTrailingPathDelimiter(TargetPath));
  if (ChildFileCount = 0) and (ChildDirCount = 1) and SameText(ExtractFileName(ChildDir), TargetName) then
    Result := ChildDir;
end;

procedure TMainForm.RestoreExtractedUnit(
  const SourceRoot,
  TargetPath,
  UnitType: string;
  DeleteBeforeApply: Boolean);
var
  Search: TSearchRec;
  SourceFile: string;
begin
  if DeleteBeforeApply then
  begin
    if SameText(UnitType, 'WinRegistry') then
      DeleteRegistryKey(TargetPath)
    else
      DeletePathRecursive(TargetPath);
  end;

  if SameText(UnitType, 'WinRegistry') then
  begin
    if FindFirst(IncludeTrailingPathDelimiter(SourceRoot) + '*.reg', faAnyFile, Search) <> 0 then
      Exit;
    try
      repeat
        if (Search.Name = '.') or (Search.Name = '..') then
          Continue;
        if (Search.Attr and faDirectory) <> 0 then
          Continue;
        ImportRegistryFile(IncludeTrailingPathDelimiter(SourceRoot) + Search.Name);
        Exit;
      until FindNext(Search) <> 0;
    finally
      FindClose(Search);
    end;
  end
  else if SameText(UnitType, 'File') then
  begin
    if FindFirst(IncludeTrailingPathDelimiter(SourceRoot) + '*', faAnyFile, Search) <> 0 then
      Exit;
    try
      repeat
        if (Search.Name = '.') or (Search.Name = '..') then
          Continue;
        if (Search.Attr and faDirectory) <> 0 then
          Continue;
        SourceFile := IncludeTrailingPathDelimiter(SourceRoot) + Search.Name;
        CopyFileToBackup(SourceFile, TargetPath);
        Exit;
      until FindNext(Search) <> 0;
    finally
      FindClose(Search);
    end;
  end
  else
    CopyDirectoryToBackup(ResolveFolderContentRoot(SourceRoot, TargetPath), TargetPath);
end;

procedure TMainForm.DeletePathRecursive(const PathValue: string);
var
  Search: TSearchRec;
  Child: string;
begin
  if FileExists(PathValue) then
  begin
    DeleteFile(PathValue);
    Exit;
  end;

  if not DirectoryExists(PathValue) then
    Exit;

  if FindFirst(IncludeTrailingPathDelimiter(PathValue) + '*', faAnyFile, Search) = 0 then
  begin
    try
      repeat
        if (Search.Name = '.') or (Search.Name = '..') then
          Continue;
        Child := IncludeTrailingPathDelimiter(PathValue) + Search.Name;
        DeletePathRecursive(Child);
      until FindNext(Search) <> 0;
    finally
      FindClose(Search);
    end;
  end;

  RemoveDir(PathValue);
end;

procedure TMainForm.RunCloudTool(const CloudAction: string);
var
  Proc: TProcess;
  Output: TStringList;
  GameName: string;
begin
  if FSelectedGame = nil then
  begin
    SetStatus('No game selected.');
    Exit;
  end;

  try
    GameName := GetGameName(FSelectedGame);
    SetStatus('Running cloud action: ' + CloudAction);

    Proc := TProcess.Create(nil);
    Output := TStringList.Create;
    try
      Proc.Executable := FindCloudToolPath;
      Proc.Parameters.Add('--action');
      Proc.Parameters.Add(CloudAction);
      Proc.Parameters.Add('--config');
      Proc.Parameters.Add(FConfigPath);
      Proc.Parameters.Add('--game');
      Proc.Parameters.Add(GameName);
      Proc.Options := [poUsePipes, poStderrToOutPut, poWaitOnExit];
      Proc.Execute;
      Output.LoadFromStream(Proc.Output);

      FInfo.Lines.BeginUpdate;
      try
        FInfo.Lines.Clear;
        FInfo.Lines.AddStrings(Output);
      finally
        FInfo.Lines.EndUpdate;
      end;

      if Proc.ExitStatus = 0 then
        SetStatus('Cloud action completed: ' + CloudAction)
      else
        SetStatus('Cloud action failed: ' + CloudAction + ' exit ' + IntToStr(Proc.ExitStatus));
    finally
      Output.Free;
      Proc.Free;
    end;
  except
    on E: Exception do
      SetStatus(E.Message);
  end;
end;

end.
