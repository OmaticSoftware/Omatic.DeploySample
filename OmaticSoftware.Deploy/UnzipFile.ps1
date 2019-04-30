param(
    [parameter(Mandatory=$True)]
    [string]$CznOutputFile,

    [parameter(Mandatory=$True)]
	[string]$UnpackPath
)

Add-Type -AssemblyName System.IO.Compression.FileSystem

# Unpack CustomizationPackage.zip  
if (-not(Test-Path $CznOutputFile))
{
    Write-Error "Did not find customization output file at expected location '$CznOutputFile', exiting"
    Exit 1
}

# Hack to handle wildcard character
$Files = @()
if ($CznOutputFile -match "\*") {
	Write-Host "Wildcard detected, attempting to grab globbed items"
	$Files = Get-ChildItem $CznOutputFile | foreach { $_.FullName }
} else {
	$Files += $CznOutputFile
}

if (-not(Test-Path $UnpackPath))
{
	New-Item $UnpackPath -ItemType Directory > $null
}
Write-Host "Extracting '$CznOutputFile' to '$UnpackPath'"

foreach ($f in $Files) {
	[System.IO.Compression.ZipFile]::ExtractToDirectory($f, $UnpackPath)
}