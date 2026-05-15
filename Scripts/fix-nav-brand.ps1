$brand = @'
        <a href="/apikey.html" class="pg-brand flex items-center gap-2.5 flex-shrink-0 min-w-0 no-underline">
            <img src="/logo.png" alt="EZOFIS" class="pg-brand-logo h-8 w-auto max-w-[100px] object-contain flex-shrink-0" onerror="this.style.display='none';var f=this.nextElementSibling;if(f)f.style.display='flex';">
            <span class="pg-logo-fallback hidden items-center gap-0.5 text-base font-bold tracking-tight whitespace-nowrap" style="display:none"><span class="text-purple-600">ez</span><span class="text-purple-500">o</span><span class="text-purple-700">fis</span></span>
            <h1 class="pg-brand-title">API Playground</h1>
        </a>
'@

$root = Join-Path $PSScriptRoot "..\wwwroot"
Get-ChildItem $root -Filter "*.html" | ForEach-Object {
    $c = [IO.File]::ReadAllText($_.FullName)
    if ($c -match 'class="pg-brand"') { return }
    if ($c -notmatch 'API Playground') { return }

    $orig = $c
    $c = [regex]::Replace($c,
        '(?s)\s*<div class="flex items-center[^"]*">\s*<img src="/logo\.png"[^>]*>\s*</div>\s*<h1[^>]*>API Playground</h1>',
        "`n$brand")

    $c = [regex]::Replace($c,
        '(?s)\s*<div class="flex items-center space-x-3"><img src="/logo\.png"[^>]*></motion.div>\s*<h1[^>]*>API Playground</h1>',
        "`n$brand")

    $c = [regex]::Replace($c,
        '(?s)\s*<div class="flex items-center space-x-3 flex-shrink-0">\s*<img src="/logo\.png"[^>]*>.*?</div>\s*<h1[^>]*>API Playground</h1>',
        "`n$brand")

    if ($c -ne $orig) {
        [IO.File]::WriteAllText($_.FullName, $c)
        Write-Host "brand: $($_.Name)"
    }
}
Write-Host "done"
