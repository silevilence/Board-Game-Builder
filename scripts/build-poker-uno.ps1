param([string]$Output = "samples/poker-uno")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repoRoot = Split-Path $PSScriptRoot
$target = [IO.Path]::GetFullPath($Output, $repoRoot)
[IO.Directory]::CreateDirectory((Join-Path $target 'assets')) | Out-Null
$sources = @()
foreach ($suit in @('clubs', 'diamonds', 'hearts', 'spades')) {
    foreach ($rank in @('A', '02', '03', '04', '05', '06', '07', '08', '09', '10', 'J', 'Q', 'K')) {
        $sources += Join-Path $repoRoot "samples/freecell/assets/card_${suit}_${rank}.png"
    }
}
# FreeCell has no hidden cards; its sibling sample carries the back from the same Kenney pack.
$sources += Join-Path $repoRoot 'samples/island-settlers/assets/card-back.png'
$cellWidth = 144; $cellHeight = 194
$atlas = [Drawing.Bitmap]::new(13 * $cellWidth, 5 * $cellHeight)
$graphics = [Drawing.Graphics]::FromImage($atlas)
$frames = [ordered]@{}
try {
    $graphics.Clear([Drawing.Color]::Transparent)
    for ($index = 0; $index -lt $sources.Count; $index++) {
        $source = [Drawing.Bitmap]::new($sources[$index])
        try {
            $x = ($index % 13) * $cellWidth + 2
            $y = [int]([Math]::Floor($index / 13) * $cellHeight + 2)
            $graphics.DrawImage($source, [Drawing.Rectangle]::new($x, $y, 140, 190))
            $name = if ($index -eq 52) { 'card_back' } else { [IO.Path]::GetFileNameWithoutExtension($sources[$index]) }
            $frames[$name] = [ordered]@{
                frame = [ordered]@{ x=$x; y=$y; w=140; h=190 }; rotated=$false; trimmed=$false
                spriteSourceSize = [ordered]@{ x=0; y=0; w=140; h=190 }; sourceSize = [ordered]@{ w=140; h=190 }
            }
        } finally { $source.Dispose() }
    }
    $atlas.Save((Join-Path $target 'assets/cards.png'), [Drawing.Imaging.ImageFormat]::Png)
} finally { $graphics.Dispose(); $atlas.Dispose() }
Copy-Item -LiteralPath (Join-Path $repoRoot 'samples/freecell/assets/License.txt') -Destination (Join-Path $target 'assets/License.txt')
[ordered]@{ frames=$frames; meta=[ordered]@{ image='cards.png'; format='RGBA8888'; size=[ordered]@{ w=1872; h=970 }; scale='1' } } |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $target 'assets/cards.json') -Encoding utf8
python (Join-Path $PSScriptRoot 'build-poker-uno.py') $target
if ($LASTEXITCODE -ne 0) { throw 'Module generation failed' }
