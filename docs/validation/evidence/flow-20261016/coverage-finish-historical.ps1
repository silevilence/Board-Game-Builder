$ErrorActionPreference='Stop'
$probe=Start-Process -FilePath 'src/Editor/bin/Release/net10.0-windows/Editor.exe' -ArgumentList '--verify-export','artifacts/flow-editor-coverage-accepted.json.project','artifacts/flow-editor-coverage-export-complete','artifacts/player-release-flow-editor' -WindowStyle Hidden -PassThru -Wait
$result=Get-Content 'artifacts/flow-editor-coverage-export-complete/result.json' -Raw | ConvertFrom-Json
if(-not $result.success) {throw $result.errors}
$probe=Start-Process -FilePath 'src/Editor/bin/Release/net10.0-windows/Editor.exe' -ArgumentList '--verify-runtime-documents','artifacts/flow-editor-format-complete.json' -WindowStyle Hidden -PassThru -Wait
$result=Get-Content 'artifacts/flow-editor-format-complete.json' -Raw | ConvertFrom-Json
if(-not $result.success) {throw $result.error}
