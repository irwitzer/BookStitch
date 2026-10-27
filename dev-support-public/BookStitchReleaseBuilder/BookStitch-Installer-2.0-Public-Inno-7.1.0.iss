; BookStitch 2.0 public release - classic Windows installer
; Target compiler: Inno Setup 7.1.0 (x64).
; Keeps the existing AppId for upgrades from version 1.x.
; The command line supplies MyAppVersion, MyVersionInfoVersion,
; SourceDir, SourceRoot and OutputDir through ISCC --define arguments.
; This file contains no machine-specific absolute paths.
; Interactive uninstall can optionally remove project data (explicit confirmation).

#ifndef MyAppVersion
  #error MyAppVersion must be supplied by the release build script
#endif
#ifndef MyVersionInfoVersion
  #error MyVersionInfoVersion must be supplied by the release build script
#endif
#ifndef SourceDir
  #error SourceDir must be supplied by the release build script
#endif
#ifndef SourceRoot
  #error SourceRoot must be supplied by the release build script
#endif
#ifndef OutputDir
  #error OutputDir must be supplied by the release build script
#endif

#define MyAppName "BookStitch"
#define MyAppPublisher "irwitzer"
#define MyAppURL "https://github.com/irwitzer/BookStitch"
#define MyAppExeName "BookStitch.exe"

[Setup]
AppId={{B19ADC40-0BDF-419C-B67C-049F064395C2}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
VersionInfoVersion={#MyVersionInfoVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
UsePreviousPrivileges=yes
LicenseFile={#SourceRoot}\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename=BookStitch-Setup-{#MyAppVersion}
SetupIconFile={#SourceRoot}\BookStitch\Assets\Icons\icon\BookStitch-Icon-OpenBook.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern dark

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "czech"; MessagesFile: "compiler:Languages\Czech.isl"
Name: "danish"; MessagesFile: "compiler:Languages\Danish.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "norwegian"; MessagesFile: "compiler:Languages\Norwegian.isl"
Name: "dutch"; MessagesFile: "compiler:Languages\Dutch.isl"
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "swedish"; MessagesFile: "compiler:Languages\Swedish.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "chinesetraditional"; MessagesFile: "compiler:Languages\ChineseTraditional.isl"
Name: "finnish"; MessagesFile: "compiler:Languages\Finnish.isl"

[CustomMessages]
; Official full .isl languages only. Unsupported Windows UI languages fall back to English.
; Each included language has its own project-data prompts.
UninstallProjectsQuestion=Delete all saved audiobook projects, including settings, permanently?
UninstallProjectsWarning=This deletion cannot be undone.
UninstallProjectsDeleteButton=Delete
UninstallProjectsNoButton=No
UninstallProjectsDeleting=Deleting saved audiobook projects and settings. This may take a while...
UninstallProjectsDeleteFailed=The project folder could not be deleted completely. Please check the remaining files in:
german.UninstallProjectsQuestion=Sollen auch alle gespeicherten Hörbuchprojekte einschließlich Einstellungen dauerhaft gelöscht werden?
german.UninstallProjectsWarning=Die Löschung kann nicht rückgängig gemacht werden.
german.UninstallProjectsDeleteButton=Löschen
german.UninstallProjectsNoButton=Nein
german.UninstallProjectsDeleting=Gespeicherte Hörbuchprojekte und Einstellungen werden gelöscht. Dies kann einige Zeit dauern...
german.UninstallProjectsDeleteFailed=Der Projektordner konnte nicht vollständig gelöscht werden. Bitte prüfen Sie die verbliebenen Dateien unter:
czech.UninstallProjectsQuestion=Mají být také všechny uložené projekty audioknih včetně nastavení trvale odstraněny?
czech.UninstallProjectsWarning=Odstranění nelze vrátit zpět.
czech.UninstallProjectsDeleteButton=Odstranit
czech.UninstallProjectsNoButton=Ne
czech.UninstallProjectsDeleting=Odstraňují se uložené projekty audioknih a nastavení. Může to chvíli trvat...
czech.UninstallProjectsDeleteFailed=Složku projektů se nepodařilo zcela odstranit. Zkontrolujte zbývající soubory v umístění:
danish.UninstallProjectsQuestion=Skal alle gemte lydbogsprojekter inklusive indstillinger også slettes permanent?
danish.UninstallProjectsWarning=Sletningen kan ikke fortrydes.
danish.UninstallProjectsDeleteButton=Slet
danish.UninstallProjectsNoButton=Nej
danish.UninstallProjectsDeleting=Gemte lydbogsprojekter og indstillinger slettes. Dette kan tage lidt tid...
danish.UninstallProjectsDeleteFailed=Projektmappen kunne ikke slettes helt. Kontrollér de resterende filer i:
spanish.UninstallProjectsQuestion=¿Se deben eliminar también de forma permanente todos los proyectos de audiolibros guardados, incluida la configuración?
spanish.UninstallProjectsWarning=La eliminación no se puede deshacer.
spanish.UninstallProjectsDeleteButton=Eliminar
spanish.UninstallProjectsNoButton=No
spanish.UninstallProjectsDeleting=Eliminando los proyectos de audiolibros guardados y la configuración. Esto puede tardar un poco...
spanish.UninstallProjectsDeleteFailed=No se pudo eliminar por completo la carpeta de proyectos. Comprueba los archivos restantes en:
french.UninstallProjectsQuestion=Voulez-vous également supprimer définitivement tous les projets de livres audio enregistrés, y compris les paramètres ?
french.UninstallProjectsWarning=Cette suppression est irréversible.
french.UninstallProjectsDeleteButton=Supprimer
french.UninstallProjectsNoButton=Non
french.UninstallProjectsDeleting=Suppression des projets de livres audio et des paramètres enregistrés. Cela peut prendre un certain temps...
french.UninstallProjectsDeleteFailed=Le dossier des projets n’a pas pu être entièrement supprimé. Vérifiez les fichiers restants dans :
italian.UninstallProjectsQuestion=Eliminare definitivamente anche tutti i progetti di audiolibri salvati, incluse le impostazioni?
italian.UninstallProjectsWarning=L’eliminazione non può essere annullata.
italian.UninstallProjectsDeleteButton=Elimina
italian.UninstallProjectsNoButton=No
italian.UninstallProjectsDeleting=Eliminazione dei progetti di audiolibri e delle impostazioni salvati. Potrebbe richiedere del tempo...
italian.UninstallProjectsDeleteFailed=Non è stato possibile eliminare completamente la cartella dei progetti. Controllare i file rimasti in:
japanese.UninstallProjectsQuestion=保存されているすべてのオーディオブックプロジェクトと設定も完全に削除しますか？
japanese.UninstallProjectsWarning=この削除は元に戻せません。
japanese.UninstallProjectsDeleteButton=削除
japanese.UninstallProjectsNoButton=いいえ
japanese.UninstallProjectsDeleting=保存されているオーディオブックプロジェクトと設定を削除しています。しばらくお待ちください...
japanese.UninstallProjectsDeleteFailed=プロジェクトフォルダーを完全に削除できませんでした。次の場所に残っているファイルを確認してください：
norwegian.UninstallProjectsQuestion=Skal alle lagrede lydbokprosjekter, inkludert innstillinger, også slettes permanent?
norwegian.UninstallProjectsWarning=Slettingen kan ikke angres.
norwegian.UninstallProjectsDeleteButton=Slett
norwegian.UninstallProjectsNoButton=Nei
norwegian.UninstallProjectsDeleting=Sletter lagrede lydbokprosjekter og innstillinger. Dette kan ta litt tid...
norwegian.UninstallProjectsDeleteFailed=Prosjektmappen kunne ikke slettes fullstendig. Kontroller gjenværende filer i:
dutch.UninstallProjectsQuestion=Moeten ook alle opgeslagen luisterboekprojecten, inclusief instellingen, definitief worden verwijderd?
dutch.UninstallProjectsWarning=Deze verwijdering kan niet ongedaan worden gemaakt.
dutch.UninstallProjectsDeleteButton=Verwijderen
dutch.UninstallProjectsNoButton=Nee
dutch.UninstallProjectsDeleting=Opgeslagen luisterboekprojecten en instellingen worden verwijderd. Dit kan even duren...
dutch.UninstallProjectsDeleteFailed=De projectmap kon niet volledig worden verwijderd. Controleer de resterende bestanden in:
polish.UninstallProjectsQuestion=Czy usunąć również trwale wszystkie zapisane projekty audiobooków wraz z ustawieniami?
polish.UninstallProjectsWarning=Tej operacji nie można cofnąć.
polish.UninstallProjectsDeleteButton=Usuń
polish.UninstallProjectsNoButton=Nie
polish.UninstallProjectsDeleting=Usuwanie zapisanych projektów audiobooków i ustawień. Może to chwilę potrwać...
polish.UninstallProjectsDeleteFailed=Nie udało się całkowicie usunąć folderu projektów. Sprawdź pozostałe pliki w:
brazilianportuguese.UninstallProjectsQuestion=Deseja também excluir permanentemente todos os projetos de audiolivros salvos, incluindo as configurações?
brazilianportuguese.UninstallProjectsWarning=Esta exclusão não pode ser desfeita.
brazilianportuguese.UninstallProjectsDeleteButton=Excluir
brazilianportuguese.UninstallProjectsNoButton=Não
brazilianportuguese.UninstallProjectsDeleting=Excluindo projetos de audiolivros e configurações salvos. Isso pode levar algum tempo...
brazilianportuguese.UninstallProjectsDeleteFailed=Não foi possível excluir completamente a pasta de projetos. Verifique os arquivos restantes em:
russian.UninstallProjectsQuestion=Удалить также все сохранённые проекты аудиокниг вместе с настройками без возможности восстановления?
russian.UninstallProjectsWarning=Это удаление нельзя отменить.
russian.UninstallProjectsDeleteButton=Удалить
russian.UninstallProjectsNoButton=Нет
russian.UninstallProjectsDeleting=Удаление сохранённых проектов аудиокниг и настроек. Это может занять некоторое время...
russian.UninstallProjectsDeleteFailed=Не удалось полностью удалить папку проектов. Проверьте оставшиеся файлы в:
swedish.UninstallProjectsQuestion=Ska även alla sparade ljudboksprojekt, inklusive inställningar, tas bort permanent?
swedish.UninstallProjectsWarning=Borttagningen kan inte ångras.
swedish.UninstallProjectsDeleteButton=Ta bort
swedish.UninstallProjectsNoButton=Nej
swedish.UninstallProjectsDeleting=Tar bort sparade ljudboksprojekt och inställningar. Det kan ta en stund...
swedish.UninstallProjectsDeleteFailed=Projektmappen kunde inte tas bort helt. Kontrollera återstående filer i:
turkish.UninstallProjectsQuestion=Kaydedilmiş tüm sesli kitap projeleri ve ayarları da kalıcı olarak silinsin mi?
turkish.UninstallProjectsWarning=Bu silme işlemi geri alınamaz.
turkish.UninstallProjectsDeleteButton=Sil
turkish.UninstallProjectsNoButton=Hayır
turkish.UninstallProjectsDeleting=Kaydedilmiş sesli kitap projeleri ve ayarlar siliniyor. Bu işlem biraz sürebilir...
turkish.UninstallProjectsDeleteFailed=Proje klasörü tamamen silinemedi. Kalan dosyaları şu konumda kontrol edin:
chinesesimplified.UninstallProjectsQuestion=是否同时永久删除所有已保存的有声书项目及设置？
chinesesimplified.UninstallProjectsWarning=此删除操作无法撤销。
chinesesimplified.UninstallProjectsDeleteButton=删除
chinesesimplified.UninstallProjectsNoButton=否
chinesesimplified.UninstallProjectsDeleting=正在删除已保存的有声书项目和设置，可能需要一些时间...
chinesesimplified.UninstallProjectsDeleteFailed=无法完全删除项目文件夹。请检查以下位置的剩余文件：
chinesetraditional.UninstallProjectsQuestion=是否也要永久刪除所有已儲存的有聲書專案及設定？
chinesetraditional.UninstallProjectsWarning=此刪除操作無法復原。
chinesetraditional.UninstallProjectsDeleteButton=刪除
chinesetraditional.UninstallProjectsNoButton=否
chinesetraditional.UninstallProjectsDeleting=正在刪除已儲存的有聲書專案與設定，這可能需要一些時間...
chinesetraditional.UninstallProjectsDeleteFailed=無法完全刪除專案資料夾。請檢查以下位置的剩餘檔案：
finnish.UninstallProjectsQuestion=Poistetaanko myös kaikki tallennetut äänikirjaprojektit ja niiden asetukset pysyvästi?
finnish.UninstallProjectsWarning=Poistoa ei voi peruuttaa.
finnish.UninstallProjectsDeleteButton=Poista
finnish.UninstallProjectsNoButton=Ei
finnish.UninstallProjectsDeleting=Poistetaan tallennettuja äänikirjaprojekteja ja asetuksia. Tämä voi kestää hetken...
finnish.UninstallProjectsDeleteFailed=Projektikansiota ei voitu poistaa kokonaan. Tarkista jäljelle jääneet tiedostot sijainnista:

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceRoot}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceRoot}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceRoot}\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\BookStitch"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\BookStitch"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,BookStitch}"; Flags: nowait postinstall skipifsilent

[Code]
const
  // Same Windows folder (MyMusic) as Environment.SpecialFolder.MyMusic in the app.
  CSIDL_MYMUSIC = $000D;

var
  DeleteProjectsAfterUninstall: Boolean;
  ProjectsFolderToDelete: String;

function ConfirmProjectDeletion(const ProjectPath: String): Boolean;
var
  Dialog: TSetupForm;
  QuestionLabel, WarningLabel, PathLabel: TNewStaticText;
  DeleteButton, NoButton: TNewButton;
begin
  Result := False;
  Dialog := CreateCustomForm(ScaleX(520), ScaleY(205), True, True);
  try
    Dialog.Caption := 'BookStitch';
    Dialog.Position := poScreenCenter;

    QuestionLabel := TNewStaticText.Create(Dialog);
    QuestionLabel.Parent := Dialog;
    QuestionLabel.Left := ScaleX(16);
    QuestionLabel.Top := ScaleY(16);
    QuestionLabel.Width := Dialog.ClientWidth - ScaleX(32);
    QuestionLabel.Height := ScaleY(48);
    QuestionLabel.AutoSize := False;
    QuestionLabel.WordWrap := True;
    QuestionLabel.ShowAccelChar := False;
    QuestionLabel.Caption := CustomMessage('UninstallProjectsQuestion');

    WarningLabel := TNewStaticText.Create(Dialog);
    WarningLabel.Parent := Dialog;
    WarningLabel.Left := ScaleX(16);
    WarningLabel.Top := ScaleY(73);
    WarningLabel.Width := Dialog.ClientWidth - ScaleX(32);
    WarningLabel.Height := ScaleY(28);
    WarningLabel.AutoSize := False;
    WarningLabel.WordWrap := True;
    WarningLabel.ShowAccelChar := False;
    WarningLabel.Caption := CustomMessage('UninstallProjectsWarning');

    PathLabel := TNewStaticText.Create(Dialog);
    PathLabel.Parent := Dialog;
    PathLabel.Left := ScaleX(16);
    PathLabel.Top := ScaleY(112);
    PathLabel.Width := Dialog.ClientWidth - ScaleX(32);
    PathLabel.Height := ScaleY(36);
    PathLabel.AutoSize := False;
    PathLabel.WordWrap := True;
    PathLabel.ShowAccelChar := False;
    PathLabel.Caption := ProjectPath;

    DeleteButton := TNewButton.Create(Dialog);
    DeleteButton.Parent := Dialog;
    DeleteButton.Width := ScaleX(114);
    DeleteButton.Height := ScaleY(26);
    DeleteButton.Left := Dialog.ClientWidth - ScaleX(16 + 114 + 10 + 114);
    DeleteButton.Top := Dialog.ClientHeight - ScaleY(40);
    DeleteButton.Caption := CustomMessage('UninstallProjectsDeleteButton');
    DeleteButton.ModalResult := mrOk;

    NoButton := TNewButton.Create(Dialog);
    NoButton.Parent := Dialog;
    NoButton.Width := ScaleX(114);
    NoButton.Height := ScaleY(26);
    NoButton.Left := Dialog.ClientWidth - ScaleX(16 + 114);
    NoButton.Top := Dialog.ClientHeight - ScaleY(40);
    NoButton.Caption := CustomMessage('UninstallProjectsNoButton');
    NoButton.ModalResult := mrCancel;
    NoButton.Default := True;
    NoButton.Cancel := True;
    Dialog.ActiveControl := NoButton;

    // No/close/ESC keeps the project data; only explicit Delete opts in.
    Result := Dialog.ShowModal = mrOk;
  finally
    Dialog.Free;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  MusicFolder: String;
begin
  // Silent uninstalls MUST leave all project data untouched.
  if UninstallSilent then
    Exit;

  case CurUninstallStep of
    usUninstall:
      begin
        // Only after Inno's standard uninstall confirmation.
        MusicFolder := GetShellFolderByCSIDL(CSIDL_MYMUSIC, False);
        if (MusicFolder = '') or (not DirExists(MusicFolder)) then
          Exit;

        // BookStitch production path is fixed; developer-only overrides are excluded.
        ProjectsFolderToDelete := AddBackslash(MusicFolder) + 'BookStitchProjects';
        if not DirExists(ProjectsFolderToDelete) then
          Exit;

        DeleteProjectsAfterUninstall := ConfirmProjectDeletion(ProjectsFolderToDelete);
      end;

    usPostUninstall:
      begin
        // Remove the projects only after Inno has removed the application.
        if DeleteProjectsAfterUninstall then
        begin
          UninstallProgressForm.StatusLabel.Caption :=
            CustomMessage('UninstallProjectsDeleting');
          UninstallProgressForm.StatusLabel.Repaint;

          if not DelTree(ProjectsFolderToDelete, True, True, True) then
            MsgBox(
              CustomMessage('UninstallProjectsDeleteFailed') + #13#10#13#10 +
              ProjectsFolderToDelete,
              mbError, MB_OK);
        end;
      end;
  end;
end;
