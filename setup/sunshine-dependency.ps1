param(
    [ValidateSet('Add','Remove')]
    [string]$Mode = 'Add'
)

$key = 'HKLM:\SYSTEM\CurrentControlSet\Services\SunshineService'
$target = 'ParsecVDisplaySvc'

if (-not (Test-Path -LiteralPath $key)) {
    Write-Output '未检测到 Sunshine 服务，跳过依赖设置'
    exit 0
}

$raw = (Get-ItemProperty -LiteralPath $key -ErrorAction SilentlyContinue).DependOnService
$deps = @($raw | Where-Object { $_ -and $_ -ne '' })

if ($Mode -eq 'Add') {
    if ($deps -notcontains $target) {
        $deps = @($deps) + @($target)
    }

    Set-ItemProperty -LiteralPath $key -Name 'DependOnService' -Value ([string[]]$deps) -Type MultiString
    Write-Output ('Sunshine 依赖已设置：' + ($deps -join ', '))
}
else {
    $deps = @($deps | Where-Object { $_ -ne $target })

    if ($deps.Count -eq 0) {
        Remove-ItemProperty -LiteralPath $key -Name 'DependOnService' -ErrorAction SilentlyContinue
    }
    else {
        Set-ItemProperty -LiteralPath $key -Name 'DependOnService' -Value ([string[]]$deps) -Type MultiString
    }

    Write-Output 'Sunshine 依赖已移除'
}

exit 0
