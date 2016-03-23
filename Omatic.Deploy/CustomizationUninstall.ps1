<# 
This script will perform uninstall
#>
param 
(
	[parameter()]
	[string]$LogFile="",
	
	[parameter()]
	[string]$ManifestFile="",

	[parameter()]
	[switch]$NonInteractive
)

# Logging setup
$LogFormat = "{0:yyyyMMdd HHmmss} {1} {2}"
if ($LogFile.Length -eq 0) {
	$LogFile = Join-Path $PSScriptRoot -ChildPath "UninstallLog.txt"
} else {
	$LogFile = [System.IO.Path]::GetFullPath($LogFile)
}

# Credit for the following: https://rkeithhill.wordpress.com/2013/04/05/powershell-script-that-relaunches-as-admin/
If (-NOT ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator"))
{   
	Write-Host "Not running as admin, elevating permissions."
	Write-Host "LogFile: $LogFile."
	[string[]]$argList = @("& '$($MyInvocation.MyCommand.Path)'")
	foreach ($argItem in $MyInvocation.BoundParameters.GetEnumerator()) {
		$argList += "-$($argItem.Key)"
		$argList += "'$($argItem.Value)'"
	}
	$argList += $MyInvocation.UnboundArguments
	#"No Administrative rights, it will display a popup window asking user for Admin rights"
	write-Host $argList
	Start-Process "$psHome\powershell.exe" -Verb runAs -WorkingDirectory $pwd -ArgumentList $argList
	break
}

# Logging functions
function Log-Error {
	param ([string]$msg)
	Write-Error $msg
	Add-Content $LogFile -Value ($LogFormat -f (Get-Date), "ERROR", $msg)
}

function Log-Warning {
	param ([string]$msg)
	Write-Warning $msg
	Add-Content $LogFile -Value ($LogFormat -f (Get-Date), "WARN", $msg)
}

function Log-Info {
	param ([string]$msg)
	Add-Content $LogFile -Value ($LogFormat -f (Get-Date), "INFO", $msg)
}

function Check-DirectoryIsEmpty {
	param ([string]$dir) 

	$isEmpty = $True
	foreach ($item in (Get-ChildItem $dir -Recurse -Force)) {
		if (-not($item -is [System.IO.DirectoryInfo])) {
			$isEmpty = $False
			break
		}
	}

	return $isEmpty
}



# Start script
Log-Info "Starting customization uninstall."

if ($ManifestFile.Length -eq 0) {
	$ManifestFile = [System.IO.Path]::Combine($PSScriptRoot, 'BBCRMCustomInstallManifest.txt')
}

$warnings = @()

try
{
	if (-not(Test-Path $ManifestFile)) {
		throw "Could not find manifest file at '$ManifestFile'"
	}

	# Keep track of a few extra things to clean up that might not be in the manifest
	$cleanupArr = @(
		[System.IO.Path]::Combine($PSScriptRoot, '..\..\vroot\bin\custom'),
		[System.IO.Path]::Combine($PSScriptRoot, '..\..\vroot\browser\htmlforms\custom'),
		[System.IO.Path]::Combine($PSScriptRoot, 'RevisionDlls'),
		[System.IO.Path]::Combine($PSScriptRoot, 'SQL'),
		[System.IO.Path]::Combine($PSScriptRoot, 'SystemRoles'),
		[System.IO.Path]::Combine($PSScriptRoot, 'Tasks'),
		[System.IO.Path]::Combine($PSScriptRoot, 'BBCRMCustomInstallManifest.txt')
	)

	foreach ($line in (Get-Content $ManifestFile)) {
		if ($line.Length -gt 0) {
			if (Test-Path $line) {
				$item = Get-Item $line
				if ($item -is [System.IO.DirectoryInfo]) {
					Log-Info "Encountered directory '$line', queueing for later cleanup"
					$cleanupArr += $item
				} else {
					Log-Info "Removing '$line'"
					Remove-Item $line
				}
			} else {
				$msg = "Did not find expected file: '$line'"
				$warnings += $msg
				Log-Warning $msg
			}
		}
	}

	foreach ($cleanup in $cleanupArr) {
		if (Test-Path $cleanup) {
			$cleanupItem = Get-Item $cleanup
			if ($cleanupItem -is [System.IO.DirectoryInfo]) {
				if (Check-DirectoryIsEmpty $cleanupItem) {
					Log-Info "Removing empty directory '$cleanupItem'"
					Remove-Item $cleanupItem -Force -Recurse
				} else {
					Log-Info "Directory '$cleanupItem' is not empty, will not remove"
				}
			}
		}
	}
	
} 
catch 
{
	Log-Error $_.Exception
	if (-not($NonInteractive)) {
		Read-Host "Error encountered, see '$LogFile' for detail.  Press any key to continue..." > $null
	} else {
		throw
	}
}
#finally
#{
#    if (Test-Path $tempPath) {
#        Log-Info "Cleaning up '$tempPath'"
#        Remove-Item $tempPath -Recurse -Force
#    }
#}