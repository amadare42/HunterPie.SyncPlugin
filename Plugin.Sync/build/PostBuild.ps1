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

function Update-ModuleJson() {
    Set-Location -Path $root
    $version = (Get-ChildItem -Path "Plugin.Sync.dll").VersionInfo.FileVersion
    $branchName = Get-BranchName

    (Get-Content module.json).`
        Replace('$VERSION$', $version).`
        Replace('$BRANCH$', $branchName) |
        Set-Content module.json
    Write-Host "Updated module.json > Version: $version; Branch: $branchName"
}

Update-ModuleJson