param([string]$root)
$ErrorActionPreference = 'Stop'

function Get-BranchName() {
    try {
        $branch = git rev-parse --abbrev-ref HEAD

        if ($branch -eq "HEAD") {
            Write-Host " detached HEAD, using SHA"
            $branch = git rev-parse --short HEAD
            return $branch
        }
        else {
            return $branch
        }
    } catch {
        Write-Host " error on getting branch name, using 'master'"
        return "master"
    }
}

function Get-Version($path) {
    return (Get-ChildItem -Path $path).VersionInfo.FileVersion
}

function Update-ModuleJson() {
    Set-Location -Path $root
    $version = Get-Version("Plugin.Sync.dll")
    $branchName = Get-BranchName

    $content = (Get-Content module.json).`
        Replace('$BRANCH$', $branchName)
    $content =  $content | % { [Regex]::Replace($_, '\$version:([^$]+)\$', { Get-Version $args[0].Groups[1] }) }
    $content =  $content | % { [Regex]::Replace($_, '\$hash:([^$]+)\$', { $(Get-FileHash $args[0].Groups[1]).Hash }) }

    Set-Content module.json $content

    Write-Host "Updated module.json > Version: $version; Branch: $branchName"
}

Update-ModuleJson