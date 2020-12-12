# modified from https://stackoverflow.com/a/36200661/2223729
param([string]$path)
$ErrorActionPreference = 'Stop'
$pattern = '\[assembly: (AssemblyVersion|AssemblyFileVersion)\("(.*)"\)\]'
(Get-Content $path) | ForEach-Object{
    if($_ -match $pattern){
        # We have found the matching line
        # Edit the version number and put back.
        $fileVersion = [version]$matches[2]
        $newVersion = "{0}.{1}.{2}.{3}" -f $fileVersion.Major, $fileVersion.Minor, $fileVersion.Build, ($fileVersion.Revision + 1)
        '[assembly: {0}("{1}")]' -f $matches[1], $newVersion
        Write-Host ('Assembly version updated to {0}' -f $newVersion)
    } else {
        # Output line as is
        $_
    }
} | Set-Content $path