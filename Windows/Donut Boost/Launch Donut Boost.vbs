Set WshShell = CreateObject("WScript.Shell")
scriptDir = Left(WScript.ScriptFullName, InStrRev(WScript.ScriptFullName, "\"))
exePath = scriptDir & "publish\win-x64\DonutBoost.exe"
dllPath = scriptDir & "publish\win-x64\DonutBoost.dll"
Set fso = CreateObject("Scripting.FileSystemObject")
If fso.FileExists(exePath) Then
    WshShell.Run """" & exePath & """", 1, False
Else
    WshShell.Run "dotnet """ & dllPath & """", 0, False
End If
