$root = Join-Path $PSScriptRoot "..\wwwroot"
$skip = @('kyc-documentation.html')
$headInject = @"
    <link rel="stylesheet" href="/css/playground-v6.css">
    <link rel="stylesheet" href="/theme.css">
    <script src="/theme.js"></script>
"@
$shellScript = '    <script src="/js/playground-shell.js"></script>'

Get-ChildItem -Path $root -Filter "*.html" -File | ForEach-Object {
    if ($skip -contains $_.Name) { return }
    $c = [IO.File]::ReadAllText($_.FullName)
    $orig = $c

    if ($c -notmatch 'playground-v6\.css') {
        if ($c -match '(<script src="https://cdn\.tailwindcss\.com"></script>)') {
            $c = $c -replace '(<script src="https://cdn\.tailwindcss\.com"></script>)', "`$1`n$headInject"
        } elseif ($c -match '(</head>)') {
            $c = $c -replace '(</head>)', "$headInject`n`$1"
        }
    }

    if ($c -notmatch 'playground-shell\.js' -and $c -match '</body>') {
        $c = $c -replace '</body>', "$shellScript`n</body>"
    }

    if ($c -match '<body') {
        $c = $c -replace '\s*gradient-animate', ''
        if ($c -notmatch '\bpg-app\b') {
            $c = $c -replace '(<body\s+class=")', '$1pg-app '
        }
        $c = $c -replace 'background-size:\s*400%\s*400%;?', ''
    }

    $c = $c -replace 'absolute left-1/2 -translate-x-1/2 pointer-events-none">API Playground', 'absolute left-[calc(50%+128px)] -translate-x-1/2 pointer-events-none">API Playground'

    if ($c -notmatch 'my-api-keys\.html' -and $c -match 'Developer Docs</a>') {
        $c = $c -replace '(Developer Docs</a>)', "`$1`n            <a href=`"/my-api-keys.html`" class=`"px-3 py-1 rounded text-sm font-semibold text-gray-700 hover:bg-gray-100`">My API Keys</a>"
    }

    if ($c -match 'fonts\.googleapis\.com/css2\?family=Inter' -and $c -notmatch 'Poppins') {
        $c = $c -replace 'family=Inter[^&"]+', 'family=Inter:wght@400;500;600;700&family=Poppins:wght@600;700'
    }

    if ($c -ne $orig) {
        [IO.File]::WriteAllText($_.FullName, $c)
        Write-Host "updated $($_.Name)"
    }
}
Write-Host "done"
