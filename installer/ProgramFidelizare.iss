; Programul de instalare pentru Program de fidelizare.
;
; Se compilează cu build-installer.ps1, care publică întâi executabilul și apoi
; rulează compilatorul Inno Setup. Rezultatul ajunge în installer\Output.
;
; Programul merge în Program Files, unde Windows nu permite scrierea. Datele merg
; în folderul comun ProgramData, nu în cel al utilizatorului: la magazin, casierul
; și patronul pot intra în Windows cu conturi diferite și trebuie să vadă aceiași
; clienți. Legătura o face fidelizare.config, scris lângă executabil la instalare.

#define AppName "Program de fidelizare"
#define AppExeName "ProgramFidelizare.exe"
#define AppPublisher "Cristian Blaga"
#define AppUrl "https://github.com/CristiBlaga134/ProgramFidelizare"
#define PublishDir "..\publish"
#define DataFolder "ProgramFidelizare"

#ifndef AppVersion
  #define AppVersion GetVersionNumbersString(PublishDir + "\" + AppExeName)
#endif

[Setup]
; Identificatorul rămâne același la toate versiunile, ca o versiune nouă să se
; instaleze peste cea veche în loc să apară de două ori în lista de programe.
AppId={{2B7F5F33-0400-41DC-BF7A-DD686B8C467D}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\ProgramFidelizare
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; .NET 10 nu mai rulează pe Windows 7 sau 8.
MinVersion=10.0
OutputDir=Output
OutputBaseFilename=ProgramFidelizare-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
; La actualizare, programul deschis se închide singur, ca executabilul să poată
; fi înlocuit. Nu se repornește: casierul îl deschide când e pregătit.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "romanian"; MessagesFile: "Romanian.isl"

; Mesajele apărute în Inno Setup după traducerea românească neoficială (6.1).
; Fără ele, compilatorul le folosește pe cele în engleză.
[Messages]
romanian.DownloadingLabel2=Se descarcă fișierele...
romanian.ExtractingLabel=Se extrag fișierele...
romanian.ButtonStopExtraction=&Oprește extragerea
romanian.StopExtraction=Sigur vreți să opriți extragerea?
romanian.ErrorExtractionAborted=Extragere întreruptă
romanian.ErrorExtractionFailed=Extragerea a eșuat: %1
romanian.RetryCancelSelectAction=Alegeți acțiunea
romanian.RetryCancelRetry=Înce&arcă din nou
romanian.RetryCancelCancel=Renunță
romanian.StatusDownloadFiles=Se descarcă fișierele...
romanian.SourceVerificationFailed=Verificarea fișierului sursă a eșuat: %1
romanian.VerificationSignatureDoesntExist=Fișierul de semnătură „%1” nu există
romanian.VerificationSignatureInvalid=Fișierul de semnătură „%1” nu este valid
romanian.VerificationKeyNotFound=Fișierul de semnătură „%1” folosește o cheie necunoscută
romanian.VerificationFileNameIncorrect=Numele fișierului este greșit
romanian.VerificationFileTagIncorrect=Eticheta fișierului este greșită
romanian.VerificationFileSizeIncorrect=Dimensiunea fișierului este greșită
romanian.VerificationFileHashIncorrect=Amprenta fișierului este greșită
romanian.ErrorDownloading=A apărut o eroare la descărcarea unui fișier:
romanian.ErrorExtracting=A apărut o eroare la extragerea unei arhive:
romanian.ArchiveIncorrectPassword=Parola este greșită
romanian.ArchiveIsCorrupted=Arhiva este deteriorată
romanian.ArchiveUnsupportedFormat=Formatul arhivei nu este acceptat

[CustomMessages]
romanian.DataNote=Datele magazinului se păstrează în %1. Dezinstalarea programului nu le șterge.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Dirs]
; Toți utilizatorii calculatorului pot scrie aici: baza de date, jurnalul WAL al
; SQLite și jurnalul de erori se creează la rulare, de contul care e conectat.
; Folderul nu se șterge la dezinstalare, ca să nu se piardă datele magazinului.
Name: "{commonappdata}\{#DataFolder}"; Permissions: users-modify; Flags: uninsneveruninstall

[Files]
Source: "{#PublishDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{app}\fidelizare.config"

[Code]
const
  ConfigFileName = 'fidelizare.config';

{ Scrie fișierul care trimite programul la folderul comun de date. Un fișier
  existent nu se atinge: la o actualizare poate indica deja o bază de date de
  pe rețea, pusă acolo pentru mai multe case de marcat. }
procedure WriteConfigIfMissing();
var
  ConfigPath: String;
  Content: String;
begin
  ConfigPath := ExpandConstant('{app}\' + ConfigFileName);
  if FileExists(ConfigPath) then
    Exit;

  Content :=
    '# Creat de programul de instalare.' + #13#10 +
    '# Baza de date sta in folderul comun al calculatorului, ca toti' + #13#10 +
    '# utilizatorii Windows sa vada aceiasi clienti.' + #13#10 +
    '# Pentru mai multe case de marcat, se inlocuieste cu o cale de retea.' + #13#10 +
    'database = ' + ExpandConstant('{commonappdata}\{#DataFolder}\fidelizare.db') + #13#10;

  if not SaveStringToFile(ConfigPath, Content, False) then
    MsgBox('Fișierul de configurare nu a putut fi scris: ' + ConfigPath, mbError, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    WriteConfigIfMissing();
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo + NewLine + NewLine;
  if MemoTasksInfo <> '' then
    Result := Result + MemoTasksInfo + NewLine + NewLine;
  { Rândul nu poate începe cu o paranteză pătrată: Inno Setup l-ar citi ca secțiune nouă. }
  Result := Result + FmtMessage(CustomMessage('DataNote'), [ExpandConstant('{commonappdata}\{#DataFolder}')]);
end;
