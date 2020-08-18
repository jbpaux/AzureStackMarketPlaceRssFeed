$regex = 'PackageReference Include="([^"]*)" Version="([^"]*)"'

ForEach ($file in get-childitem . -recurse | Where-Object {$_.extension -like "*proj"})
{
    $packages = Get-Content $file.FullName |
        select-string -pattern $regex -AllMatches | 
        ForEach-Object {$_.Matches} | 
        ForEach-Object {$_.Groups[1].Value.ToString()}| 
        Sort-Object -Unique

    ForEach ($package in $packages)
    {
        write-host "Update $file package :$package"  -foreground 'magenta'
        $fullName = $file.FullName
        Invoke-Expression "dotnet add $fullName package $package"
    }
}