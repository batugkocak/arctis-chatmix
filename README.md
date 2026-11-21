# Arctis 7X+ ChatMix for Windows

Makes the ChatMix dial on the **SteelSeries Arctis 7X+** work on a PC with **SteelSeries Sonar**.

In PC mode, SteelSeries GG does not read the dial for this headset (`get-chatmix-values not implemented` in `gg-errorlog.txt`). The USB dongle still sends the dial position, so this small background program reads it and sets the volume of the Sonar **Gaming** and **Chat** devices.

## How it works

- The dongle (USB `1038:2216`, interface `MI_05`) sends HID report `45 <game%> <chat%>` when the dial moves. Values are 0–100.
- Center: `100 / 100`. Game end: `100 / 0`. Chat end: `0 / 100`.
- At center the sensor jitters (about 95–100), so values of 90 and above count as 100. Values below 90 scale linearly.
- The program sets the Windows volume of `SteelSeries Sonar - Gaming` and `SteelSeries Sonar - Chat`.
- It runs without a window, reconnects when the headset or dongle reconnects, and allows only one instance.

## Requirements

- Windows 10/11
- SteelSeries GG with Sonar enabled
- Dongle switch set to **PC**
- Games output to `Sonar - Gaming`, voice chat (Discord etc.) outputs to `Sonar - Chat`

## Build

Uses the C# compiler that ships with Windows (.NET Framework 4.x). Nothing to install.

```powershell
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /out:ArctisChatMix.exe ChatMix.cs
```

## Run at startup

```powershell
$dst = "$env:LOCALAPPDATA\ArctisChatMix"
New-Item -ItemType Directory -Force $dst | Out-Null
Copy-Item ArctisChatMix.exe $dst
$s = (New-Object -ComObject WScript.Shell).CreateShortcut("$([Environment]::GetFolderPath('Startup'))\ArctisChatMix.lnk")
$s.TargetPath = "$dst\ArctisChatMix.exe"; $s.Save()
Start-Process "$dst\ArctisChatMix.exe"
```

To uninstall, end `ArctisChatMix` in Task Manager, then delete the folder and the shortcut in `shell:startup`.

## Other headsets

`HidListen.cs` prints all HID reports from the dongle. Use it to find the report format for a different model, then change `Device` in `ChatMix.cs`.

```powershell
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /out:HidListen.exe HidListen.cs
.\HidListen.exe 60
```

Change the `vid_1038&pid_2216` filter in `HidListen.cs` to match your device.

Not affiliated with SteelSeries.
