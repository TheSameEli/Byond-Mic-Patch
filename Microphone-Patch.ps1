param(
    [ValidateSet('Apply', 'Restore', 'Status')]
    [string]$Mode = 'Status',
    [string]$ByondDirectory = ''
)
$ErrorActionPreference = 'Stop'

function Get-Sha([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}
function Convert-Hex([string]$Hex) {
    $result = New-Object byte[] ($Hex.Length / 2)
    for ($i = 0; $i -lt $result.Length; $i++) {
        $result[$i] = [Convert]::ToByte($Hex.Substring($i * 2, 2), 16)
    }
    return ,$result
}

try {
    if (!$ByondDirectory) {
        $ByondDirectory = Join-Path ${env:ProgramFiles(x86)} 'BYOND\bin'
    }
    $ByondDirectory = [IO.Path]::GetFullPath($ByondDirectory)
    $target = Join-Path $ByondDirectory 'byondwin.dll'
    if (!(Test-Path -LiteralPath $target)) {
        $ByondDirectory = Join-Path $ByondDirectory 'bin'
        $target = Join-Path $ByondDirectory 'byondwin.dll'
    }
    if (!(Test-Path -LiteralPath $target)) { throw 'Не найден byondwin.dll. Укажите папку BYOND параметром -ByondDirectory.' }
    $backup = "$target.before-microphone-patch"
    $manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Raw | ConvertFrom-Json
    $hash = Get-Sha $target
    $matching = @($manifest | Where-Object { $_.originalSha256 -eq $hash -or $_.patchedSha256 -eq $hash })
    if ($matching.Count -ne 1) { throw 'Эта сборка BYOND не поддерживается. Файл не изменён: контрольная сумма не совпала.' }
    $entry = $matching[0]
    $patched = $hash -eq $entry.patchedSha256
    Write-Host "BYOND $($entry.version): $target"
    Write-Host $(if ($patched) { 'Патч микрофона установлен.' } else { 'Оригинальный файл, патч не установлен.' })
    if ($Mode -eq 'Status') { exit 0 }
    if (($Mode -eq 'Apply' -and $patched) -or ($Mode -eq 'Restore' -and !$patched)) { exit 0 }

    $running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -and ([IO.Path]::GetDirectoryName($_.Path) -ieq $ByondDirectory) } catch { $false }
    })
    if ($running.Count) { throw 'Сначала закройте все приложения BYOND из этой папки, включая главное окно BYOND, и повторите установку.' }

    if ($Mode -eq 'Restore') {
        if (!(Test-Path -LiteralPath $backup) -or (Get-Sha $backup) -ne $entry.originalSha256) {
            throw 'Не найдена подходящая оригинальная резервная копия. Откат не выполнен.'
        }
        [IO.File]::Copy($backup, $target, $true)
        if ((Get-Sha $target) -ne $entry.originalSha256) { throw 'Не удалось проверить восстановленный файл.' }
        Write-Host 'Оригинальный BYOND восстановлен.'
        exit 0
    }

    [byte[]]$data = [IO.File]::ReadAllBytes($target)
    [byte[]]$expected = Convert-Hex $entry.originalBytes
    [byte[]]$replacement = Convert-Hex $entry.patchedBytes
    for ($i = 0; $i -lt $expected.Length; $i++) {
        if ($data[$entry.offset + $i] -ne $expected[$i]) { throw 'Обработчик разрешений отличается. Патч не применён.' }
    }
    if (Test-Path -LiteralPath $backup) {
        if ((Get-Sha $backup) -ne $entry.originalSha256) { throw 'Резервная копия не соответствует этой версии. Она сохранена без изменений.' }
    } else { [IO.File]::Copy($target, $backup, $false) }
    [Array]::Copy($replacement, 0, $data, [int]$entry.offset, $replacement.Length)
    [IO.File]::WriteAllBytes($target, $data)
    if ((Get-Sha $target) -ne $entry.patchedSha256) {
        [IO.File]::Copy($backup, $target, $true)
        throw 'Проверка патча не прошла. Оригинальный файл восстановлен.'
    }
    Write-Host 'Готово. При запросе микрофона WebView2 покажет штатное разрешение. Тихое включение микрофона не добавлено.'
    Write-Host 'Для отката запустите Restore-Patch.cmd.'
} catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
