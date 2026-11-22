Set fso = CreateObject("Scripting.FileSystemObject")
Set shellApp = CreateObject("Shell.Application")

scriptDir = Left(WScript.ScriptFullName, InStrRev(WScript.ScriptFullName, "\"))

targetExe = scriptDir & "publish\current\DonutBoost.exe"

If fso.FileExists(targetExe) Then
    shellApp.ShellExecute targetExe, "", scriptDir, "open", 1
Else
    targetDll = scriptDir & "publish\current\DonutBoost.dll"

    If fso.FileExists(targetDll) Then
        shellApp.ShellExecute "dotnet", """" & targetDll & """", scriptDir, "open", 1
    Else
        MsgBox "The latest Donut Boost files are missing. Reinstall Donut Boost from the official website.", 16, "Donut Boost"
    End If
End If
