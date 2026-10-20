# Undoes the oldest AI turn in the sandbox and prints the folder before/after. No HTTP, no Gemini.
$bin = (Resolve-Path "$PSScriptRoot\..\HearthFileManager\bin\Debug\net48").Path
foreach ($f in 'Newtonsoft.Json.dll','System.Data.SQLite.dll','cshttp.dll','cshttp.web.dll','cshttp.server.dll','HearthFileManager.exe') { [Reflection.Assembly]::LoadFrom("$bin\$f") | Out-Null }
$ad = "$PSScriptRoot\sandbox\App_Data"
$fs = New-Object HearthFileManager.engine.FsService($ad, "$ad\public")
"before: " + (($fs.List("") | % { $_.Path }) -join ", ")
$id = (Get-ChildItem "$ad\ai-undo" | Sort-Object CreationTime | Select -First 1).Name
"undo $id -> " + ([HearthFileManager.engine.Ai.AiUndo]::Undo($fs, $id) -join ", ")
"after: " + (($fs.List("") | % { $_.Path }) -join ", ") + " | App_Data: " + (($fs.List("App_Data") | % { $_.Path }) -join ", ")
