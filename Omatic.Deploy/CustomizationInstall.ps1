<#
   *) execute msbuild against MSBuild\Custom\CustomInstall.proj
  #>
param 
(
	[parameter()]
	[string]$LogFile="",

	[parameter()]
	[string]$InstanceName="",

	[parameter()]
	[switch]$InstallWithRestore,

	[parameter()]
	[switch]$NoInstall,

    [parameter()]
    [switch]$NonInteractive
)

# Powershell v2 doesn't includ PSScriptRoot
if ($PSVersionTable.PSVersion.Major -eq 2) {
	$PSScriptRoot = Split-Path $MyInvocation.MyCommand.Path -Parent
}

# Logging setup
$LogFormat = "{0:yyyyMMdd HHmmss} {1} {2}"
if ($LogFile.Length -eq 0) {
    $LogFile = Join-Path $PSScriptRoot -ChildPath "InstallLog.txt"
} else {
    $LogFile = [System.IO.Path]::GetFullPath($LogFile)
}

# Credit for the following: https://rkeithhill.wordpress.com/2013/04/05/powershell-script-that-relaunches-as-admin/
If (-NOT ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator"))
{   
	Write-Host "Not running as admin, elevating permissions."
	Write-Host "LogFile: $LogFile."
	[string[]]$argList = @("& '$($MyInvocation.MyCommand.Path)'")
    $argList += "-LogFile '$LogFile'"
    $argList += "-InstanceName '$InstanceName'"
	if ($InstallWithRestore) { $argList += "-InstallWithRestore" }
    if ($NoInstall) { $argList += "-NoInstall" }
    if ($NonInteractive) { $argList += "-NonInteractive" }
    #"No Administrative rights, it will display a popup window asking user for Admin rights"
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


function Get-CrmInstallPath {
    param([string]$crmInstanceName)

    # Assumes 32-bit BB installer on 64-bit machine
    $INSTALLERREGKEY = "HKLM:\Software\Wow6432Node\Blackbaud\AppFx"

    $installPath = ''

    $bbInstalls = Get-ChildItem -Path $INSTALLERREGKEY
    if ($bbInstalls.Length -eq 0) {
	    $errMsg = "ERROR: Did not find any BB installs at reg key '$INSTALLERREGKEY'"
	    Throw $errMsg
    } else {
        Log-Info "Found $($bbInstalls.Length) Blackbaud\AppFx install(s)"
		foreach ($bbInstall in $bbInstalls) 
		{
			$regEntry = Get-Item $bbInstall.PSPath
            $installName = $regEntry.GetValue("InstallName")
            Log-Info "Examining InstallName: '$installName'"
			if (($installName.Length -eq 0 -and $crmInstanceName.Length -eq 0) -or ($installName -ieq $crmInstanceName))
            { 
				$installPath = $regEntry.GetValue("InstallPath")
				Log-Info "Found installPath: '$installPath' for instance: '$InstanceName'"
			}
		}
    }

    return $installPath
}


function Unpack-Files {
    param(
        [parameter()]
        [string]$zipFile,

        [parameter()]
        [string]$unpackFolder
    )

    Log-Info "Unpacking '$zipFile' to '$unpackFolder'"

	[IO.Compression.ZipFile]::ExtractToDirectory($zipFile, $unpackFolder)
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

function Copy-CustomizationFiles {
	param(
		[parameter()]
		[string]$sourceFolder,

		[parameter()]
		[string]$crmInstallPath,

		[parameter()]
		[string]$customInstallPath,

		[parameter()]
		[string]$manifestFile
	)

	Log-Info "Preparing to copy customization files from '$sourceFolder' to CRM installation at '$crmInstallPath', writing copied files to '$manifestFile'"
		
	$folderMap = @{}

	# BinDlls folder
	$src = [System.IO.Path]::Combine($sourceFolder, 'Package', 'DeployFiles', 'BinDlls')
	if (-not(Test-Path $src)) {
		throw "Did not find BinDlls folder at expected location: '$src'"
	}
	$tgt = [System.IO.Path]::Combine($crmInstallPath, 'bbAppFx', 'vroot', 'bin', 'custom')
	$folderMap.Add($src, $tgt)

	# HtmlForms folder
	$src = [System.IO.Path]::Combine($sourceFolder, 'Package', 'DeployFiles', 'HtmlForms')
	if (-not(Test-Path $src)) {
		throw "Did not find HtmlForms folder at expected location: '$src'"
	}
	$tgt = [System.IO.Path]::Combine($crmInstallPath, 'bbAppFx', 'vroot', 'browser', 'htmlforms')
	$folderMap.Add($src, $tgt)

	# RevisionDlls folder
	$src = [System.IO.Path]::Combine($sourceFolder, 'Package', 'DeployFiles', 'RevisionDlls')
	if (-not(Test-Path $src)) {
		throw "Did not find RevisionDlls folder at expected location: '$src'"
	}
	$tgt = [System.IO.Path]::Combine($customInstallPath, 'RevisionDlls')
	$folderMap.Add($src, $tgt)

	# SQL folder
	$src = [System.IO.Path]::Combine($sourceFolder, 'Package', 'DeployFiles', 'Sql')
	if (-not(Test-Path $src)) {
		throw "Did not find SQL folder at expected location: '$src'"
	}
	$tgt = [System.IO.Path]::Combine($customInstallPath, 'SQL')
	$folderMap.Add($src, $tgt)

	# System Roles folder
	$src = [System.IO.Path]::Combine($sourceFolder, 'Package', 'DeployFiles', 'SystemRoles')
	if (-not(Test-Path $src)) {
		throw "Did not find SystemRoles folder at expected location: '$src'"
	}
	$tgt = [System.IO.Path]::Combine($customInstallPath, 'SystemRoles')
	$folderMap.Add($src, $tgt)

	# Tasks folder
	$src = [System.IO.Path]::Combine($sourceFolder, 'Package', 'DeployFiles', 'Tasks')
	if (-not(Test-Path $src)) {
		throw "Did not find Tasks folder at expected location: '$src'"
	}
	$tgt = [System.IO.Path]::Combine($customInstallPath, 'Tasks')
	$folderMap.Add($src, $tgt)

	# Customization install support files
	$src = [System.IO.Path]::Combine($sourceFolder, 'Package', 'DeployFiles', 'InstallSupportFiles')
	if (-not(Test-Path $src)) {
		throw "Did not find config.xml file at expected location: '$src'"
	}
	$folderMap.Add($src, $customInstallPath)

	# Do the copying, write each copied item to the manifest file
    if (-not(Test-Path $manifestFile)) { 
        New-Item $manifestFile -ItemType File > $null
    }
    $manifestDirectory = (Get-Item $manifestFile).Directory
	foreach ($map in $folderMap.GetEnumerator()) {
        $source = $map.Key
        $target = $map.Value

        $robocopyLogFile = [System.IO.Path]::Combine($manifestDirectory, 'robocopylog.txt')

		Log-Info "Copying from '$source' to '$target', writing copied files to '$robocopyLogFile'" 
        
        robocopy $source $target /s /is /np /njh /njs /ns /nc /fp /xx /log:$robocopyLogFile *>> $LogFle

		$counter = 0
		foreach ($line in (Get-Content $robocopyLogFile)) {
			$updated = $line.Trim().Replace($source, $target)
			if ($updated.Length -gt 0) {
				Add-Content $manifestFile -Value $updated
				$counter += 1
			}
		}

		Log-Info "Copied $counter item(s)"
	}
		
	Log-Info "Copying manifest '$manifestFile' to '$customInstallPath'"
	Copy-Item -Path $manifestFile -Destination $customInstallPath -Force
}

function Uninstall-CustomizationFiles {
	param(
		[parameter()]
		[string]$customInstallPath,
		[parameter()]
		[string]$manifestFile
	)

	$uninstallScript = [System.IO.Path]::Combine($customInstallPath, 'CustomizationUninstall.ps1')
	if (-not(Test-Path $uninstallScript)) {
		Log-Info "Did not find uninstall script at '$uninstallScript', skipping uninstall."
		return
	} 
	Log-Info "Found uninstall script at '$uninstallScript', performing uninstall"

    $cmd = "& '$uninstallScript' -LogFile '$LogFile' -NonInteractive"

	Invoke-Expression $cmd
}

function Run-MSBuildInstall {
	param(
		[parameter()]
		[string]$customInstallPath
	)

	$logPath = [System.IO.Path]::Combine((Get-Item $LogFile).DirectoryName, 'MSBuildLog')
	if (-not(Test-Path $logPath)) {
		New-Item $logPath -ItemType Directory > $null
	}

	$msBuildPath = [System.IO.Path]::Combine($customInstallPath, 'CustomInstall.proj')
	if ($InstallWithRestore) {
		Log-Info "Running MSBuild with DB restore"
		$msBuildParams = "/t:InstallWithRestore"
	} else {
		$msBuildParams = "/t:Install"
	}

	Log-Info "Preparing to invoke MSBuild, logs will be written to '$logPath'"

	$msBuildResult = $True
	if ($NonInteractive) {
		$msBuildResult = Invoke-MsBuild -Path $msBuildPath -MsBuildParameters $msBuildParams -BuildLogDirectoryPath $logPath
	} else {
		$msBuildResult = Invoke-MsBuild -Path $msBuildPath -MsBuildParameters $msBuildParams -BuildLogDirectoryPath $logPath -ShowBuildWindowAndPromptForInputBeforeClosing
	}

	if (-not $msBuildResult) {
		throw "MSBuild failed"
	}
}


# Start script
Log-Info "********** START INSTALLATION **********"

Add-Type -Assembly "System.IO.Compression.FileSystem"

Import-Module -Name ([System.IO.Path]::Combine($PSScriptRoot, "Invoke-MsBuild.psm1"))

# Establish extract path here because we'll clean it up in our 'finally' block
$tempPath = [System.IO.Path]::Combine(([System.IO.Path]::GetTempPath()), 'BBCRMCustomInstall')
if (Test-Path $tempPath) {
	try {
        Remove-Item $tempPath -Recurse -Force
    } catch {
        Log-Warning "Could not remove '$($tempPath)': $($_.Exception)"
    }
}

try
{
	$installPath = Get-CrmInstallPath $InstanceName
    if ($installPath.Length -eq 0) {
        throw "Failed to find install path for CRM instance '$InstanceName'"
    }

    $zipFile = [System.IO.Path]::Combine($PSScriptRoot, 'DeployFiles.zip')
    if (-not(Test-Path $zipFile)) {
        throw "Failed to find zip file '$zipFile'"
    }

    Unpack-Files $zipFile $tempPath

	$manifestFile = [System.IO.Path]::Combine($tempPath, 'BBCRMCustomInstallManifest.txt')

    $customInstallPath = [System.IO.Path]::Combine($installPath, 'bbAppFx', 'MSBuild', 'Custom')
	
    Log-Info "Using custom install path: '$customInstallPath'"

    if (-not(Test-Path $customInstallPath)) {
        New-Item $customInstallPath -Type Directory > $null
    } else {
        Uninstall-CustomizationFiles $customInstallPath $manifestFile
    }

	Copy-CustomizationFiles $tempPath $installPath $customInstallPath $manifestFile

	if ($NoInstall) {
		Log-Info "NoInstall command line parameter set, skipping installation."
	} else {
		Run-MSBuildInstall $customInstallPath 
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
finally
{
    if (Test-Path $tempPath) {
        Log-Info "Cleaning up '$tempPath'"
        Remove-Item $tempPath -Recurse -Force
    }
	Log-Info "********** END INSTALLATION **********"
}