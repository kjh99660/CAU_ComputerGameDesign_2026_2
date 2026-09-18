Add-Type -AssemblyName System.Drawing

$outputPath = Join-Path $PSScriptRoot 'core-system-class-diagram.png'
$canvas = [System.Drawing.Bitmap]::new(2600, 1900)
$graphics = [System.Drawing.Graphics]::FromImage($canvas)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$graphics.Clear([System.Drawing.ColorTranslator]::FromHtml('#F6F9FD'))

$fontPath = 'C:\Windows\Fonts\malgun.ttf'
$fontFamily = [System.Drawing.FontFamily]::new('Malgun Gothic')
$titleFont = [System.Drawing.Font]::new($fontFamily, 40, [System.Drawing.FontStyle]::Bold)
$sectionFont = [System.Drawing.Font]::new($fontFamily, 21, [System.Drawing.FontStyle]::Bold)
$nameFont = [System.Drawing.Font]::new($fontFamily, 24, [System.Drawing.FontStyle]::Bold)
$bodyFont = [System.Drawing.Font]::new($fontFamily, 17, [System.Drawing.FontStyle]::Regular)
$smallFont = [System.Drawing.Font]::new($fontFamily, 16, [System.Drawing.FontStyle]::Regular)
$pillFont = [System.Drawing.Font]::new($fontFamily, 18, [System.Drawing.FontStyle]::Bold)

function Color([string]$hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function DrawText([string]$value, [System.Drawing.Font]$font, [string]$color, [float]$x, [float]$y, [float]$w, [float]$h, [string]$alignment = 'Near') {
    $brush = [System.Drawing.SolidBrush]::new((Color $color))
    $format = [System.Drawing.StringFormat]::new()
    $format.Alignment = [System.Drawing.StringAlignment]::$alignment
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $graphics.DrawString($value, $font, $brush, [System.Drawing.RectangleF]::new($x, $y, $w, $h), $format)
    $format.Dispose()
    $brush.Dispose()
}

function DrawBox([float]$x, [float]$y, [float]$w, [float]$h, [string]$name, [string]$detail, [string]$accent) {
    $shadowPath = RoundRect ($x + 5) ($y + 7) $w $h 20
    $shadowBrush = [System.Drawing.SolidBrush]::new((Color '#E3EAF4'))
    $graphics.FillPath($shadowBrush, $shadowPath)
    $shadowBrush.Dispose(); $shadowPath.Dispose()
    $path = RoundRect $x $y $w $h 20
    $fill = [System.Drawing.SolidBrush]::new((Color '#FFFFFF'))
    $border = [System.Drawing.Pen]::new((Color '#D5E0ED'), 2)
    $graphics.FillPath($fill, $path)
    $graphics.DrawPath($border, $path)
    $fill.Dispose(); $border.Dispose(); $path.Dispose()
    $bar = [System.Drawing.SolidBrush]::new((Color $accent))
    $graphics.FillRectangle($bar, $x + 18, $y + 22, 7, $h - 44)
    $bar.Dispose()
    DrawText $name $nameFont '#16283F' ($x + 42) ($y + 18) ($w - 60) 42
    DrawText $detail $bodyFont '#50657D' ($x + 42) ($y + 68) ($w - 62) ($h - 80)
}

function DrawRoute([float[]]$coords, [string]$color, [bool]$dashed = $false) {
    $pen = [System.Drawing.Pen]::new((Color $color), 3.5)
    if ($dashed) { $pen.DashPattern = [float[]]@(7, 5) }
    for ($i = 0; $i -lt $coords.Length - 2; $i += 2) {
        $graphics.DrawLine($pen, $coords[$i], $coords[$i + 1], $coords[$i + 2], $coords[$i + 3])
    }
    $last = $coords.Length - 2
    $dx = $coords[$last] - $coords[$last - 2]
    $dy = $coords[$last + 1] - $coords[$last - 1]
    $length = [Math]::Sqrt($dx * $dx + $dy * $dy)
    if ($length -gt 0) {
        $ux = $dx / $length; $uy = $dy / $length
        $ax = $coords[$last]; $ay = $coords[$last + 1]
        $wing = 10; $depth = 23
        $points = [System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new($ax, $ay),
            [System.Drawing.PointF]::new(($ax - $ux * $depth - $uy * $wing), ($ay - $uy * $depth + $ux * $wing)),
            [System.Drawing.PointF]::new(($ax - $ux * $depth + $uy * $wing), ($ay - $uy * $depth - $ux * $wing))
        )
        $brush = [System.Drawing.SolidBrush]::new((Color $color))
        $graphics.FillPolygon($brush, $points)
        $brush.Dispose()
    }
    $pen.Dispose()
}

function DrawPill([float]$x, [float]$y, [float]$w, [string]$name) {
    $path = RoundRect $x $y $w 74 14
    $fill = [System.Drawing.SolidBrush]::new((Color '#FFFFFF'))
    $border = [System.Drawing.Pen]::new((Color '#C7D8EE'), 2)
    $graphics.FillPath($fill, $path)
    $graphics.DrawPath($border, $path)
    $fill.Dispose(); $border.Dispose(); $path.Dispose()
    DrawText $name $pillFont '#24466D' ($x + 8) ($y + 5) ($w - 16) 64 'Center'
}

try {
    DrawText 'Core 시스템 클래스 관계도' $titleFont '#12243B' 100 48 1600 70
    DrawText '현재 구현 기준  |  Tick은 프레임이 아닌 라운드 세대 식별자' $sectionFont '#60758D' 104 121 1850 42
    DrawText '구성 및 런타임 서비스' $sectionFont '#2775D1' 110 205 850 45

    # 서비스 연결과 주요 참조 관계를 카드 뒤에 그린다.
    DrawRoute ([float[]]@(1300, 350, 1300, 390, 350, 390, 350, 485)) '#8DB6E6'
    DrawRoute ([float[]]@(1300, 350, 1300, 390, 940, 390, 940, 485)) '#8DB6E6'
    DrawRoute ([float[]]@(1300, 350, 1300, 485)) '#8DB6E6'
    DrawRoute ([float[]]@(1300, 350, 1300, 390, 2140, 390, 2140, 485)) '#8DB6E6'
    DrawRoute ([float[]]@(350, 650, 350, 785)) '#2775D1'
    DrawRoute ([float[]]@(940, 650, 940, 785)) '#845ED6' $true
    DrawRoute ([float[]]@(1540, 650, 1540, 785)) '#2775D1'
    DrawRoute ([float[]]@(2140, 650, 2140, 785)) '#2775D1'
    DrawRoute ([float[]]@(1180, 550, 1300, 550)) '#2775D1'
    DrawRoute ([float[]]@(1780, 560, 1900, 560)) '#2775D1'
    DrawRoute ([float[]]@(590, 600, 650, 600, 650, 860, 700, 860)) '#2775D1'
    DrawRoute ([float[]]@(1300, 600, 1240, 600, 1240, 860, 1180, 860)) '#2775D1'
    DrawRoute ([float[]]@(1180, 620, 1205, 620, 1205, 1095, 1185, 1095)) '#2775D1'
    DrawRoute ([float[]]@(1540, 650, 1540, 1015)) '#2775D1'
    DrawRoute ([float[]]@(1780, 610, 1850, 610, 1850, 1085, 1900, 1085)) '#2775D1'
    DrawRoute ([float[]]@(940, 1170, 940, 1205, 365, 1205, 365, 1300)) '#2775D1'
    DrawRoute ([float[]]@(900, 1350, 580, 1350)) '#845ED6' $true
    DrawRoute ([float[]]@(1920, 1350, 2010, 1350)) '#845ED6' $true
    DrawRoute ([float[]]@(1685, 1400, 1685, 1485)) '#0FA3A5'

    DrawBox 1000 220 600 130 'CoreBootstrap' "Unity Update → 요청 Drain → 4계층 순차 호출`nFixedUpdate → Physics 계층" '#2775D1'
    DrawBox 110 490 480 160 'InitializationCoordinator' "IRoundInitializable 순차 실행`n완료 요청을 이벤트 버스에 등록" '#2775D1'
    DrawBox 700 490 480 160 'EventManager' "IGameEventBus 구현`n요청 FIFO · 상태 알림 즉시 전달" '#845ED6'
    DrawBox 1300 490 480 160 'GameStateManager' "GameState와 Tick 소유`n요청 검증 · Reset 승인 · 전환 알림" '#2775D1'
    DrawBox 1900 490 480 160 'RoundResetCoordinator' "생산 차단 → 순차 Reset`n완료 시 상태 관리자에 보고" '#2775D1'

    DrawBox 110 790 480 150 'IRoundInitializable' "초기화 참여 시스템의 계약`nInitializeRound(tick)" '#0FA3A5'
    DrawBox 700 790 480 150 'IGameEventBus' "EnqueueRequest · RequestDequeued`nPublishStateChanged · StateChanged" '#845ED6'
    DrawBox 1300 790 480 150 'GameStateRules' "현재 상태와 요청 타입으로`n허용 전이 및 Reset 가능 여부 판정" '#0FA3A5'
    DrawBox 1900 790 480 150 'IRoundResettable' "StopProducing()`nResetRound(tick)" '#0FA3A5'

    DrawBox 700 1020 480 150 'FrameRequestQueue' "요청과 발행 프레임 저장`n다음 프레임부터 FIFO 전달" '#D78935'
    DrawBox 1300 1020 480 150 'GameState' "Initializing → Ready → Battle`nFinishing → Result 등 9개 상태" '#D78935'
    DrawBox 1900 1020 480 150 'GameStateChangedEvent' "이전 상태 · 현재 상태 · Tick`n전환 직후 전달되는 불변 클래스" '#D78935'

    DrawText '요청 클래스 계층' $sectionFont '#0E9397' 110 1225 850 45
    DrawBox 150 1300 430 100 'IGameRequest' 'Tick 제공 계약' '#845ED6'
    DrawBox 900 1300 430 100 'GameRequest' '공통 Tick 보관 · 추상 클래스' '#0FA3A5'
    DrawText '일반 C# 계층 계약' $sectionFont '#0E9397' 1450 1225 850 45
    DrawBox 1450 1300 470 100 'StateAwareNode' '상태 알림 전달 · 실행 필터 캐시' '#0FA3A5'
    DrawBox 2010 1300 470 100 'IGameLoopNode' 'OnGameStateChanged · Update 계약' '#845ED6'

    $panel = RoundRect 100 1485 2400 305 24
    $panelFill = [System.Drawing.SolidBrush]::new((Color '#EAF3FB'))
    $panelBorder = [System.Drawing.Pen]::new((Color '#C9DCEC'), 2)
    $graphics.FillPath($panelFill, $panel); $graphics.DrawPath($panelBorder, $panel)
    $panelFill.Dispose(); $panelBorder.Dispose(); $panel.Dispose()
    DrawText 'CoreBootstrap → 계층 → 하위 요소' $sectionFont '#315B7D' 142 1500 1200 42
    $names = @(
        'PlayerLayer', 'EnemyLayer', 'UiLayer', 'PhysicsLayer',
        'PlayerInput / Action', 'EnemyDecision / Action', 'BattleHud / Dialogue', 'PhysicsState / Body'
    )
    for ($i = 0; $i -lt $names.Count; $i++) {
        $col = $i % 4; $row = [Math]::Floor($i / 4)
        DrawPill (142 + $col * 590) (1560 + $row * 100) 540 $names[$i]
    }

    DrawText '실선 화살표: 참조·호출·전달' $smallFont '#2775D1' 130 1820 520 40
    DrawText '점선 화살표: 인터페이스 구현' $smallFont '#845ED6' 720 1820 560 40
    DrawText '청록 화살표: 클래스 상속' $smallFont '#0E9397' 1380 1820 560 40
    DrawText '※ PhysicsLayer.FixedUpdate는 상태 요소와 Body 요소를 순서대로 호출' $smallFont '#60758D' 130 1860 1700 28

    $canvas.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Output $outputPath
}
finally {
    $graphics.Dispose()
    $canvas.Dispose()
    $titleFont.Dispose(); $sectionFont.Dispose(); $nameFont.Dispose()
    $bodyFont.Dispose(); $smallFont.Dispose(); $pillFont.Dispose()
    $fontFamily.Dispose()
}
