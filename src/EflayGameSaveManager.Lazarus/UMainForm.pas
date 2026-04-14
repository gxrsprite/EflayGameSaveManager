unit UMainForm;

{$mode objfpc}{$H+}

interface

uses
  Classes, SysUtils, Forms, Controls, StdCtrls, ExtCtrls, Grids, Dialogs,
  Process, fpjson, jsonparser;

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
    FRunButton: TButton;
    FOpenGameFolderButton: TButton;
    FOpenSaveFolderButton: TButton;
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
    procedure RunClicked(Sender: TObject);
    procedure OpenGameFolderClicked(Sender: TObject);
    procedure OpenSaveFolderClicked(Sender: TObject);
    procedure OpenConfigClicked(Sender: TObject);
    function FindConfigPath: string;
    function ReadRuntimeForcedDeviceName(const ConfigPath: string): string;
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
    procedure OpenFolderForPath(const PathValue: string);
    procedure CopyPathToBackup(const SourcePath, DestinationPath: string);
    procedure CopyDirectoryToBackup(const SourceDir, DestinationDir: string);
    procedure CopyFileToBackup(const SourceFile, DestinationFile: string);
    procedure RunCloudTool(const CloudAction: string);
  public
    constructor Create(TheOwner: TComponent); override;
    destructor Destroy; override;
  end;

var
  MainForm: TMainForm;

implementation

const
  ConfigFileName = 'GameSaveManager.config.json';
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
  FCloudStatusButton.Width := 120;
  FCloudStatusButton.Height := 32;
  FCloudStatusButton.OnClick := @CloudStatusClicked;

  FCloudUploadButton := TButton.Create(Self);
  FCloudUploadButton.Parent := CloudButtons;
  FCloudUploadButton.Caption := 'Upload Current Save To Cloud';
  FCloudUploadButton.Left := 130;
  FCloudUploadButton.Top := 4;
  FCloudUploadButton.Width := 230;
  FCloudUploadButton.Height := 32;
  FCloudUploadButton.OnClick := @CloudUploadClicked;

  FCloudRestoreButton := TButton.Create(Self);
  FCloudRestoreButton.Parent := CloudButtons;
  FCloudRestoreButton.Caption := 'Restore Current Cloud Save';
  FCloudRestoreButton.Left := 370;
  FCloudRestoreButton.Top := 4;
  FCloudRestoreButton.Width := 230;
  FCloudRestoreButton.Height := 32;
  FCloudRestoreButton.OnClick := @CloudRestoreClicked;

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

  FOpenGameFolderButton := TButton.Create(Self);
  FOpenGameFolderButton.Parent := LocalButtons;
  FOpenGameFolderButton.Caption := 'Game Folder';
  FOpenGameFolderButton.Left := 404;
  FOpenGameFolderButton.Top := 4;
  FOpenGameFolderButton.Width := 98;
  FOpenGameFolderButton.OnClick := @OpenGameFolderClicked;

  FOpenSaveFolderButton := TButton.Create(Self);
  FOpenSaveFolderButton.Parent := LocalButtons;
  FOpenSaveFolderButton.Caption := 'Save Folder';
  FOpenSaveFolderButton.Left := 508;
  FOpenSaveFolderButton.Top := 4;
  FOpenSaveFolderButton.Width := 96;
  FOpenSaveFolderButton.OnClick := @OpenSaveFolderClicked;

  FOpenConfigButton := TButton.Create(Self);
  FOpenConfigButton.Parent := LocalButtons;
  FOpenConfigButton.Caption := 'Config';
  FOpenConfigButton.Left := 610;
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
  RunCloudTool('status');
end;

procedure TMainForm.CloudUploadClicked(Sender: TObject);
begin
  RunCloudTool('upload-current');
end;

procedure TMainForm.CloudRestoreClicked(Sender: TObject);
begin
  RunCloudTool('restore-current');
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
