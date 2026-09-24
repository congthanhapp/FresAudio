[Setup]
SetupIconFile=FresAudio.ico
AppName=FresAudio
AppVerName=FresAudio
AppPublisher=Công Thành
AppCopyright=Copyright © 2026 Công Thành
DefaultDirName={autopf}\FresAudio
DefaultGroupName=FresAudio
UninstallDisplayIcon={app}\FresAudio.exe
UninstallDisplayName=FresAudio
Compression=lzma2
SolidCompression=yes
OutputDir=.
OutputBaseFilename=FresAudio_Setup
PrivilegesRequired=lowest
WizardStyle=modern
DisableProgramGroupPage=yes
DisableReadyPage=yes
DisableDirPage=no
DisableWelcomePage=no
WizardImageFile=Welcome.bmp
WizardSmallImageFile=Small.bmp

[Files]
Source: "PublishApp\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Languages]
Name: "vietnamese"; MessagesFile: "Vietnamese.isl"

[Messages]
WelcomeLabel2=Chương trình sẽ cài đặt [name] lên máy tính của bạn.%n%nFresAudio là phần mềm nghe nhạc đơn giản, hỗ trợ phát nhạc offline và tải nhạc từ các nền tảng trực tuyến phổ biến.%n%nPhát triển bởi Công Thành.

[Icons]
Name: "{group}\FresAudio"; Filename: "{app}\FresAudio.exe"
Name: "{autodesktop}\FresAudio"; Filename: "{app}\FresAudio.exe"

[Run]
Filename: "{app}\FresAudio.exe"; Description: "Khởi động FresAudio"; Flags: nowait postinstall skipifsilent
Filename: "ms-settings:defaultapps"; Description: "Đặt FresAudio làm ứng dụng phát nhạc mặc định"; Flags: shellexec nowait postinstall skipifsilent unchecked

[Registry]
Root: HKCU; Subkey: "Software\Classes\.mp3\OpenWithProgids"; ValueType: string; ValueName: "FresAudio.mp3"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.flac\OpenWithProgids"; ValueType: string; ValueName: "FresAudio.mp3"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.wav\OpenWithProgids"; ValueType: string; ValueName: "FresAudio.mp3"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\FresAudio.mp3"; ValueType: string; ValueName: ""; ValueData: "FresAudio Audio File"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\FresAudio.mp3\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\FresAudio.exe,0"
Root: HKCU; Subkey: "Software\Classes\FresAudio.mp3\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\FresAudio.exe"" ""%1"""