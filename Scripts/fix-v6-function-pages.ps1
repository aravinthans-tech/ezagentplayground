$pages = @(
    "formdetails.html", "subformdetails.html", "subformfields.html",
    "subformsubmitarchive.html", "getdatafromsalesforce.html", "my-api-keys.html"
)
$root = Join-Path $PSScriptRoot "..\wwwroot"

foreach ($name in $pages) {
    $path = Join-Path $root $name
    if (-not (Test-Path $path)) { continue }
    $c = [IO.File]::ReadAllText($path)

    # Remove large inline <style> block (keep head clean)
    $c = [regex]::Replace($c, '(?s)\s*<style>.*?</style>\s*', "`n    <link rel=`"stylesheet`" href=`"/css/playground-animations.css`">`n")

    # Ensure V6 loads LAST (after Tailwind + theme)
    if ($c -notmatch 'playground-v6\.css.*</head>') {
        $c = $c -replace '(</head>)', "    <link rel=`"stylesheet`" href=`"/css/playground-v6.css`">`n`$1"
    }
    # Remove duplicate v6 link in middle of head if present
    $c = [regex]::Replace($c, '(?s)\s*<link rel="stylesheet" href="/css/playground-v6\.css">\s*<link rel="stylesheet" href="/theme\.css">', "`n    <link rel=`"stylesheet`" href=`"/theme.css`">")

    # Submit buttons → purple primary
    $c = $c -replace 'bg-gradient-to-r from-cyan-500 to-teal-600 hover:from-cyan-600 hover:to-teal-700', 'pg-btn-primary'
    $c = $c -replace 'bg-gradient-to-r from-cyan-500 to-teal-600', 'pg-btn-primary'

    # Page header strip — flat background
    $c = $c -replace 'sticky top-0 z-20 bg-gradient-to-br from-gray-50 via-blue-50 to-purple-50 py-1', 'py-1'

    [IO.File]::WriteAllText($path, $c)
    Write-Host "updated $name"
}

Write-Host "done"
