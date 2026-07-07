$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

function New-Color([string]$hex) {
  return [System.Drawing.ColorTranslator]::FromHtml($hex)
}

function New-RoundedPath([float]$x, [float]$y, [float]$width, [float]$height, [float]$radius) {
  $diameter = $radius * 2
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
  $path.AddArc($x + $width - $diameter, $y, $diameter, $diameter, 270, 90)
  $path.AddArc($x + $width - $diameter, $y + $height - $diameter, $diameter, $diameter, 0, 90)
  $path.AddArc($x, $y + $height - $diameter, $diameter, $diameter, 90, 90)
  $path.CloseFigure()
  return $path
}

function Set-HighQuality([System.Drawing.Graphics]$graphics) {
  $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
}

function Draw-CatalogTray([System.Drawing.Graphics]$graphics, [float]$size, [bool]$roundBackground) {
  $blue = New-Color "#0F6CBD"
  $blueLight = New-Color "#1C88E5"
  $surface = New-Color "#FFFFFF"
  $accentSoft = New-Color "#D6E9FF"

  if ($roundBackground) {
    $backgroundBrush = New-Object System.Drawing.SolidBrush($blue)
    $graphics.FillEllipse($backgroundBrush, 0, 0, $size, $size)
    $backgroundBrush.Dispose()
  } else {
    $outerX = $size * 0.10
    $outerY = $size * 0.09
    $outerW = $size * 0.80
    $outerH = $size * 0.82
    $outerRadius = $size * 0.18
    $outerPath = New-RoundedPath $outerX $outerY $outerW $outerH $outerRadius
    $gradientBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
      [System.Drawing.PointF]::new($outerX, $outerY),
      [System.Drawing.PointF]::new($outerX + $outerW, $outerY + $outerH),
      $blueLight,
      $blue
    )
    $graphics.FillPath($gradientBrush, $outerPath)
    $gradientBrush.Dispose()
    $outerPath.Dispose()
  }

  $bodyX = if ($roundBackground) { $size * 0.18 } else { $size * 0.20 }
  $bodyY = if ($roundBackground) { $size * 0.22 } else { $size * 0.24 }
  $bodyW = $size * 0.64
  $bodyH = $size * 0.28
  $bodyRadius = $size * 0.08
  $bodyPath = New-RoundedPath $bodyX $bodyY $bodyW $bodyH $bodyRadius
  $bodyBrush = New-Object System.Drawing.SolidBrush($surface)
  $graphics.FillPath($bodyBrush, $bodyPath)
  $bodyBrush.Dispose()
  $bodyPath.Dispose()

  $linePen = New-Object System.Drawing.Pen($blue, [Math]::Max(2, $size * 0.05))
  $linePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
  $linePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
  $graphics.DrawLine($linePen, $bodyX + $size * 0.07, $bodyY + $size * 0.10, $bodyX + $bodyW - $size * 0.07, $bodyY + $size * 0.10)
  $graphics.DrawLine($linePen, $bodyX + $size * 0.07, $bodyY + $size * 0.20, $bodyX + $bodyW - $size * 0.18, $bodyY + $size * 0.20)
  $linePen.Dispose()

  $pinBrush = New-Object System.Drawing.SolidBrush($surface)
  $pinSize = $size * 0.08
  foreach ($pinY in @(
    ($bodyY + $size * 0.08),
    ($bodyY + $size * 0.20)
  )) {
    $graphics.FillEllipse($pinBrush, $bodyX - $pinSize * 0.80, $pinY, $pinSize, $pinSize)
    $graphics.FillEllipse($pinBrush, $bodyX + $bodyW - $pinSize * 0.20, $pinY, $pinSize, $pinSize)
  }
  $pinBrush.Dispose()

  $legBrush = New-Object System.Drawing.SolidBrush($accentSoft)
  $legY = if ($roundBackground) { $size * 0.66 } else { $size * 0.68 }
  $legW = $size * 0.07
  $legH = $size * 0.12
  foreach ($legX in @(
    ($size * 0.36),
    ($size * 0.47),
    ($size * 0.58)
  )) {
    $legPath = New-RoundedPath $legX $legY $legW $legH ($size * 0.02)
    $graphics.FillPath($legBrush, $legPath)
    $legPath.Dispose()
  }
  $legBrush.Dispose()
}

function Save-Png([int]$width, [int]$height, [string]$path, [scriptblock]$draw) {
  $bitmap = New-Object System.Drawing.Bitmap($width, $height)
  $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
  Set-HighQuality $graphics
  & $draw $graphics $width $height
  $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $graphics.Dispose()
  $bitmap.Dispose()
}

function Draw-Splash([System.Drawing.Graphics]$graphics, [int]$width, [int]$height) {
  $surface = New-Color "#F6F9FC"
  $shadow = New-Color "#2A0F6CBD"
  $graphics.Clear($surface)

  $minSide = [Math]::Min($width, $height)
  $iconSize = [Math]::Round($minSide * 0.24)
  $iconX = ($width - $iconSize) / 2
  $iconY = ($height - $iconSize) / 2 - ($minSide * 0.03)

  $shadowBrush = New-Object System.Drawing.SolidBrush($shadow)
  $graphics.FillEllipse($shadowBrush, $iconX + $iconSize * 0.08, $iconY + $iconSize * 0.80, $iconSize * 0.84, $iconSize * 0.22)
  $shadowBrush.Dispose()

  $state = $graphics.Save()
  $graphics.TranslateTransform($iconX, $iconY)
  Draw-CatalogTray $graphics $iconSize $false
  $graphics.Restore($state)
}

$root = Resolve-Path (Join-Path $PSScriptRoot "..")

$iconTargets = @{
  "android\app\src\main\res\mipmap-mdpi\ic_launcher.png" = 48
  "android\app\src\main\res\mipmap-hdpi\ic_launcher.png" = 72
  "android\app\src\main\res\mipmap-xhdpi\ic_launcher.png" = 96
  "android\app\src\main\res\mipmap-xxhdpi\ic_launcher.png" = 144
  "android\app\src\main\res\mipmap-xxxhdpi\ic_launcher.png" = 192
}

foreach ($entry in $iconTargets.GetEnumerator()) {
  $target = Join-Path $root $entry.Key
  $size = [int]$entry.Value
  Save-Png $size $size $target {
    param($graphics, $width, $height)
    $graphics.Clear([System.Drawing.Color]::Transparent)
    Draw-CatalogTray $graphics $width $false
  }
}

foreach ($entry in $iconTargets.GetEnumerator()) {
  $target = Join-Path $root ($entry.Key -replace "ic_launcher\.png", "ic_launcher_round.png")
  $size = [int]$entry.Value
  Save-Png $size $size $target {
    param($graphics, $width, $height)
    $graphics.Clear([System.Drawing.Color]::Transparent)
    Draw-CatalogTray $graphics $width $true
  }
}

foreach ($entry in $iconTargets.GetEnumerator()) {
  $target = Join-Path $root ($entry.Key -replace "ic_launcher\.png", "ic_launcher_foreground.png")
  $size = [int]$entry.Value
  Save-Png $size $size $target {
    param($graphics, $width, $height)
    $graphics.Clear([System.Drawing.Color]::Transparent)
    Draw-CatalogTray $graphics $width $false
  }
}

$splashTargets = @(
  "android\app\src\main\res\drawable\splash.png",
  "android\app\src\main\res\drawable-land-mdpi\splash.png",
  "android\app\src\main\res\drawable-land-hdpi\splash.png",
  "android\app\src\main\res\drawable-land-xhdpi\splash.png",
  "android\app\src\main\res\drawable-land-xxhdpi\splash.png",
  "android\app\src\main\res\drawable-land-xxxhdpi\splash.png",
  "android\app\src\main\res\drawable-port-mdpi\splash.png",
  "android\app\src\main\res\drawable-port-hdpi\splash.png",
  "android\app\src\main\res\drawable-port-xhdpi\splash.png",
  "android\app\src\main\res\drawable-port-xxhdpi\splash.png",
  "android\app\src\main\res\drawable-port-xxxhdpi\splash.png"
)

foreach ($relativePath in $splashTargets) {
  $target = Join-Path $root $relativePath
  $existing = [System.Drawing.Image]::FromFile($target)
  $width = $existing.Width
  $height = $existing.Height
  $existing.Dispose()

  Save-Png $width $height $target {
    param($graphics, $bitmapWidth, $bitmapHeight)
    Draw-Splash $graphics $bitmapWidth $bitmapHeight
  }
}
