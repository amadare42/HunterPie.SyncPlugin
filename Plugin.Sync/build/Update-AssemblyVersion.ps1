# this script increments build number of version (last number) for specified AssemblyInfo.cs
# modified from https://stackoverflow.com/a/36200661/2223729
param([string]$path)
$pattern = '\[assembly: (AssemblyVersion|AssemblyFileVersion)\("(.*)"\)\]'

$lines = (Get-Content $path) | ForEach-Object{
    if($_ -match $pattern){
        # We have found the matching line
        # Edit the version number and put back.
        $fileVersion = [version]$matches[2]
        $newVersion = "{0}.{1}.{2}.{3}" -f $fileVersion.Major, $fileVersion.Minor, $fileVersion.Build, ($fileVersion.Revision + 1)
        '[assembly: {0}("{1}")]' -f $matches[1], $newVersion
        Write-Host ('New assembly version to {0}' -f $newVersion)
    } else {
        # Output line as is
        $_
    }
}

for ($i = 0; $i -lt 10; $i++) {
    try
    {
        Set-Content $path $lines -ErrorAction Stop
        Write-Host "Updated version successfully"
        exit 0
    } catch {
        Write-Host "Error on writing, retry ($i/10)"
        Start-Sleep -Milliseconds 2000
    }
}

exit 1