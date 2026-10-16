$ErrorActionPreference = 'Stop'
dotnet tests/Engine.Tests/bin/Release/net10.0/Engine.Tests.dll cordi cordi-dispatch debug-play documents packages replay cpu author-operations author-validation author-workspace author-external startup runtime-recording runtime-entry flow-editor-model flow-editor-canvas flow-editor-properties flow-editor-validation flow-editor-playback flow-editor-conflicts flow-contract flow-validation flow-runtime flow-systems flow-players flow-protection flow-restoration flow-delivery flow-review
if ($LASTEXITCODE -ne 0) { throw 'Flow tests failed' }
$probe = Start-Process -FilePath 'src/Editor/bin/Release/net10.0-windows/Editor.exe' -ArgumentList '--verify-flow','artifacts/flow-editor-coverage-accepted.json' -WindowStyle Hidden -PassThru -Wait
$result = Get-Content 'artifacts/flow-editor-coverage-accepted.json' -Raw | ConvertFrom-Json
if (-not $result.success) { throw $result.error }
$exportProbe = Start-Process -FilePath 'src/Editor/bin/Release/net10.0-windows/Editor.exe' -ArgumentList '--verify-export','artifacts/flow-editor-coverage-accepted.json.project','artifacts/flow-editor-coverage-export-accepted','artifacts/player-release-flow-editor' -WindowStyle Hidden -PassThru -Wait
$exportResult = Get-Content 'artifacts/flow-editor-coverage-export-accepted/result.json' -Raw | ConvertFrom-Json
if (-not $exportResult.success) { throw $exportResult.errors }
$formatProbe = Start-Process -FilePath 'src/Editor/bin/Release/net10.0-windows/Editor.exe' -ArgumentList '--verify-runtime-documents','artifacts/flow-editor-format-accepted.json' -WindowStyle Hidden -PassThru -Wait
$formatResult = Get-Content 'artifacts/flow-editor-format-accepted.json' -Raw | ConvertFrom-Json
if (-not $formatResult.success) { throw $formatResult.error }
