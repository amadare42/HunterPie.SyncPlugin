# source: https://gist.github.com/Splaxi/fe168eaa91eb8fb8d62eba21736dc88a
# Download latest Haato3o/HunterPie release from github

$repo = "Haato3o/HunterPie"
$filenamePattern = "*HunterPie.zip"
$pathExtract = '{0}\..\HunterPie' -f $PSScriptRoot
$innerDirectory = $true
$preRelease = $false

Write-Host ('Downloading lastest {0} release...' -f $repo)

if ($preRelease) {
    $releasesUri = "https://api.github.com/repos/$repo/releases"
    $downloadUri = ((Invoke-RestMethod -Method GET -Uri $releasesUri)[0].assets | Where-Object name -like $filenamePattern ).browser_download_url
}
else {
    $releasesUri = "https://api.github.com/repos/$repo/releases/latest"
    $downloadUri = ((Invoke-RestMethod -Method GET -Uri $releasesUri).assets | Where-Object name -like $filenamePattern ).browser_download_url
}

$pathZip = Join-Path -Path $([System.IO.Path]::GetTempPath()) -ChildPath $(Split-Path -Path $downloadUri -Leaf)

Invoke-WebRequest -Uri $downloadUri -Out $pathZip

Remove-Item -Path $pathExtract -Recurse -Force -ErrorAction SilentlyContinue

if ($innerDirectory) {
    $tempExtract = Join-Path -Path $([System.IO.Path]::GetTempPath()) -ChildPath $((New-Guid).Guid)
    Expand-Archive -Path $pathZip -DestinationPath $tempExtract -Force -Verbose
    Move-Item -Path "$tempExtract\*" -Destination $pathExtract -Force
    Remove-Item -Path $tempExtract -Force -Recurse -ErrorAction SilentlyContinue
}
else {
    Expand-Archive -Path $pathZip -DestinationPath $pathExtract -Force -Verbose
}

Remove-Item $pathZip -Force