program GameSaveManagerLite;

{$mode objfpc}{$H+}

uses
  Interfaces,
  Forms,
  UMainForm;

{$R *.res}

begin
  RequireDerivedFormResource := False;
  Application.Scaled := True;
  Application.Initialize;
  Application.CreateForm(TMainForm, MainForm);
  MainForm.Show;
  MainForm.BringToFront;
  Application.Run;
end.
