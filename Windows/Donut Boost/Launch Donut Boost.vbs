Set fso = CreateObject("Scripting.FileSystemObject")
Set shellApp = CreateObject("Shell.Application")

scriptDir = Left(WScript.ScriptFullName, InStrRev(WScript.ScriptFullName, "\"))

' 2. Check all possible locations for DonutBoost.exe
Dim candidates(4)
candidates(0) = scriptDir & "DonutBoost.exe"
candidates(1) = scriptDir & "publish\build-out\DonutBoost.exe"
candidates(2) = scriptDir & "publish\win-x64\DonutBoost.exe"
candidates(3) = scriptDir & "bin\Release\net8.0-windows\DonutBoost.exe"
candidates(4) = scriptDir & "bin\Debug\net8.0-windows\DonutBoost.exe"

targetExe = ""
For i = 0 To UBound(candidates)
    If fso.FileExists(candidates(i)) Then
        targetExe = candidates(i)
        Exit For
    End If
Next

If targetExe <> "" Then
    ' Use ShellExecute with "runas" so Windows UAC elevation succeeds cleanly without error 740
    shellApp.ShellExecute targetExe, "", scriptDir, "runas", 1
Else
    ' 3. Fallback: Check for DonutBoost.dll
    Dim dllCandidates(3)
    dllCandidates(0) = scriptDir & "DonutBoost.dll"
    dllCandidates(1) = scriptDir & "publish\build-out\DonutBoost.dll"
    dllCandidates(2) = scriptDir & "publish\win-x64\DonutBoost.dll"
    dllCandidates(3) = scriptDir & "bin\Release\net8.0-windows\DonutBoost.dll"

    targetDll = ""
    For j = 0 To UBound(dllCandidates)
        If fso.FileExists(dllCandidates(j)) Then
            targetDll = dllCandidates(j)
            Exit For
        End If
    Next

    If targetDll <> "" Then
        shellApp.ShellExecute "dotnet", """" & targetDll & """", scriptDir, "runas", 1
    Else
        MsgBox "Could not locate DonutBoost.exe. Please ensure Donut Boost is installed.", 16, "Donut Boost"
    End If
End If
