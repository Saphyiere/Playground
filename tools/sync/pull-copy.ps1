<#
.SYNOPSIS
    Pulls an app's newest copy of its data off a phone or tablet plugged into the PC, and lays its files out as the app keeps them.

.DESCRIPTION
    The apps keep copies of their data (Kit/Playground.Sync) in the device's Documents, in "<App> backups", a folder for each time
    the app was installed: a manifest per copy in copies/, and each file's content in blobs/ under its SHA-256. This finds the newest
    copy over adb, pulls what the PC doesn't have yet into <Out>\store (the first time everything, then only what changed), and
    writes the copy's files under their own names into <Out>\<App> <when> (prima.db, library\..., errors.log), each checked
    against its fingerprint, with the manifest beside them.

    -From reads a folder already on the PC instead (one copied off in Explorer, over USB): "<App> backups", or one install's folder.

    To put a copy back on a device, push its store folder, then in the app open Settings > Your data > Restore from a backup... >
    Choose a folder... and pick "<App> backups":
        adb push "<Out>\store\Installed 2026-10-01 090000" "/sdcard/Documents/Prima backups/From the PC"

.EXAMPLE
    pwsh tools/sync/pull-copy.ps1
.EXAMPLE
    pwsh tools/sync/pull-copy.ps1 -App Workout -Out D:\Copies\Workout
.EXAMPLE
    pwsh tools/sync/pull-copy.ps1 -From "E:\Documents\Prima backups"
#>
param(
    # The app whose copies to pull: its folder is "<App> backups".
    [string] $App = "Prima",
    # Where the copies go on the PC.
    [string] $Out = (Join-Path ([Environment]::GetFolderPath("MyDocuments")) "$App copies"),
    # A device's serial, when more than one is plugged in (adb devices lists them).
    [string] $Device,
    # A folder of copies already on the PC, read instead of the device.
    [string] $From,
    # adb, when it isn't on the PATH or in the Android SDK's usual places.
    [string] $Adb
)

$ErrorActionPreference = "Stop"
$folder = "/sdcard/Documents/$App backups"

function Find-Adb {
    if ($Adb) { return $Adb }
    $onPath = Get-Command adb -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    $places = @(
        $(if ($env:ANDROID_HOME) { Join-Path $env:ANDROID_HOME "platform-tools\adb.exe" }),
        (Join-Path ${env:ProgramFiles(x86)} "Android\android-sdk\platform-tools\adb.exe"),
        (Join-Path $env:LOCALAPPDATA "Android\Sdk\platform-tools\adb.exe")
    ) | Where-Object { $_ -and (Test-Path $_) }
    if ($places) { return @($places)[0] }
    throw "adb wasn't found: pass -Adb with its path (Visual Studio's is in C:\Program Files (x86)\Android\android-sdk\platform-tools)."
}

function Invoke-Adb([string[]] $Arguments) {
    $all = @()
    if ($Device) { $all += @("-s", $Device) }
    $said = & $script:adb @($all + $Arguments) 2>&1
    if ($LASTEXITCODE -ne 0) { throw "adb $($Arguments -join ' ') failed: $said" }
    return $said
}

function Get-Names([string] $Path) {
    # One name a line; nothing when the folder isn't there. The device's shell reads the path in single quotes, spaces and all.
    $said = & $script:adb @($(if ($Device) { @("-s", $Device) } else { @() }) + @("shell", "ls -1 '$Path' 2>/dev/null")) 2>$null
    return @($said | ForEach-Object { "$_".Trim() } | Where-Object { $_ })
}

function ConvertTo-Instant($Value) {
    # PowerShell 7 reads a JSON time as a DateTime by itself; Windows PowerShell leaves it a string.
    if ($Value -is [DateTimeOffset]) { return $Value }
    if ($Value -is [DateTime]) { return [DateTimeOffset] $Value }
    return [DateTimeOffset]::Parse([string] $Value, [Globalization.CultureInfo]::InvariantCulture)
}

# Where the stores are: one for each install of the app, each with its copies' manifests.
$stores = @()
if ($From) {
    $From = (Resolve-Path $From).Path
    $candidates = if (Test-Path (Join-Path $From "copies")) { @(Get-Item $From) } else { @(Get-ChildItem $From -Directory | Where-Object { Test-Path (Join-Path $_.FullName "copies") }) }
    foreach ($store in $candidates) {
        foreach ($copy in Get-ChildItem (Join-Path $store.FullName "copies") -Filter "*.json") {
            $stores += [pscustomobject]@{ Install = $store.Name; Copy = $copy.Name; Local = $store.FullName }
        }
    }
}
else {
    $script:adb = Find-Adb
    Invoke-Adb @("get-state") | Out-Null
    foreach ($install in Get-Names $folder) {
        foreach ($copy in Get-Names "$folder/$install/copies" | Where-Object { $_ -like "*.json" }) {
            $stores += [pscustomobject]@{ Install = $install; Copy = $copy; Local = Join-Path $Out "store\$install" }
        }
    }
}
if (-not $stores) { throw "No copies of $App's found in $(if ($From) { $From } else { $folder })." }

# The newest copy of all: a manifest's name is when it was taken, in UTC, so they sort across installs too.
$newest = $stores | Sort-Object Copy | Select-Object -Last 1
$local = $newest.Local
New-Item -ItemType Directory -Force (Join-Path $local "copies"), (Join-Path $local "blobs") | Out-Null
if (-not $From) {
    Invoke-Adb @("pull", "$folder/$($newest.Install)/copies/$($newest.Copy)", (Join-Path $local "copies")) | Out-Null
}
$manifest = Get-Content -Raw -Encoding UTF8 (Join-Path $local "copies\$($newest.Copy)") | ConvertFrom-Json

# The content the PC doesn't have yet: all of it in one go the first time, then each piece that's new.
if (-not $From) {
    $missing = @($manifest.files | Select-Object -ExpandProperty sha256 -Unique | Where-Object { -not (Test-Path (Join-Path $local "blobs\$_")) })
    if ($missing.Count -gt 0 -and -not (Get-ChildItem (Join-Path $local "blobs") -File)) {
        Write-Host "Pulling all of $($newest.Install) from the device..."
        Invoke-Adb @("pull", "$folder/$($newest.Install)/blobs", $local) | Out-Null
    }
    else {
        foreach ($sha in $missing) { Invoke-Adb @("pull", "$folder/$($newest.Install)/blobs/$sha", (Join-Path $local "blobs")) | Out-Null }
    }
}

# The copy's files under their own names, each checked against its fingerprint.
$made = (ConvertTo-Instant $manifest.made).ToLocalTime()
$target = Join-Path $Out "$App $($made.ToString('yyyy-MM-dd HHmm', [Globalization.CultureInfo]::InvariantCulture))"
if (-not (Test-Path $target)) {
    $partial = "$target.partial"
    if (Test-Path $partial) { Remove-Item -Recurse -Force $partial }
    foreach ($file in $manifest.files) {
        $blob = Join-Path $local "blobs\$($file.sha256)"
        if (-not (Test-Path $blob)) { throw "$($file.path) is missing from the copy ($($file.sha256))." }
        if ((Get-FileHash -Algorithm SHA256 $blob).Hash.ToLowerInvariant() -ne $file.sha256) { throw "$($file.path) in the copy is damaged." }
        $path = Join-Path $partial ($file.path -replace '/', '\')
        New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
        Copy-Item $blob $path
        (Get-Item $path).LastWriteTimeUtc = (ConvertTo-Instant $file.modified).UtcDateTime
    }
    Copy-Item (Join-Path $local "copies\$($newest.Copy)") (Join-Path $partial "manifest.json")
    Move-Item $partial $target
}

$size = ($manifest.files | Measure-Object -Property size -Sum).Sum
Write-Host ("{0}'s copy from {1} ({2:0.#} MB, {3} files, version {4}) is in {5}" -f $App, $made.ToString("d MMM yyyy, HH:mm", [Globalization.CultureInfo]::InvariantCulture), ($size / 1000000), @($manifest.files).Count, $manifest.version, $target)
