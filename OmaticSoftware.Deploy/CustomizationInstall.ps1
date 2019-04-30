<#
.SYNOPSIS

Copies CRM customization files to appropriate folders, optionally executes CRM customization installation.

.PARAMETER LogFile

Full path to log file for installation.  Defaults to "InstallLog.txt" in the script's folder.

.PARAMETER InstanceName

BB CRM instance name to use.  Defaults to "" (i.e. blank).

.PARAMETER InstallWithRestore

Runs MSBuild installation with "InstallWithRestore" target, which restores the template DB.

.PARAMETER NoInstall

Bypasses MSBuild installation, making this a copy-only install.

.PARAMETER NonInteractive

Runs script without any user prompting.

  #>
param 
(
	[parameter()]
	[string]$LogFile="",

	[parameter(Mandatory=$true,HelpMessage="BB CRM instance name to use.  Use empty string for unnamed instance.")]
	[AllowEmptyString()]
	[string]$InstanceName,

	[parameter()]
	[switch]$InstallWithRestore,

	[parameter()]
	[switch]$NoInstall,

	[parameter()]
	[switch]$NonInteractive,

	[parameter()]
	[switch]$SkipBBDW=$True,

    [parameter()]
    [string]$BBDWUserPassword=""

)

# Logging setup
$LogFormat = "{0:yyyyMMdd HHmmss} {1} {2}"
if ($LogFile.length -eq 0) {
  $LogFileName = ("CustomizationInstallLog_{0:yyyyMMddHHmmss}.txt" -f (Get-Date))
  $LogFile = [IO.Path]::Combine($PSScriptRoot, $LogFileName)
}

$LogFile = [IO.Path]::GetFullPath($LogFile)

# Logging functions
function Log-Error {
	param ([string]$msg)
	Write-Error $msg
	if ($LogFile.Length -ne 0) {
		Add-Content $LogFile -Value ($LogFormat -f (Get-Date), "ERROR", $msg)
	}
}

function Log-Warning {
	param ([string]$msg)
	Write-Warning $msg
	if ($LogFile.Length -ne 0) {
		Add-Content $LogFile -Value ($LogFormat -f (Get-Date), "WARN", $msg)
	}
}

function Log-Info {
	param ([string]$msg)
	Write-Host $msg
	if ($LogFile.Length -ne 0) {
		Add-Content $LogFile -Value ($LogFormat -f (Get-Date), "INFO", $msg)
	}
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
                break;
			}
		}
	}

	return $installPath
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


function Get-AdditionalCopyFolders {
	param(
		[parameter()]
		[string]$localConfigFile
	)

    if (-Not (Test-Path $localConfigFile))
    {
        throw "Did not find local config file: $localConfigFile"
    }

    Log-Info "Get-AdditionalCopyFolder: Reading $localConfigFile"

    $ns = @{ns="http://schemas.microsoft.com/developer/msbuild/2003"}
    $xPath = '//ns:Project/ns:ItemGroup/ns:CopyToVroot/@Include'
    $values = Select-Xml -Path $localConfigFile -XPath $xPath -Namespace $ns |% { $_.Node.Value }

    return $values
}

function Get-BbdwPasswordFromConfig {
    param(
        [parameter()]
        [string]$localConfigFile
    )
    
    if (-Not (Test-Path $localConfigFile))
    {
        throw "Did not find local config file: $localConfigFile"
    }

    Log-Info "Get-BbdwPasswordFromConfig: Reading $localConfigFile"

    $ns = @{ns="http://schemas.microsoft.com/developer/msbuild/2003"}
    $xPath = '//ns:Project/ns:PropertyGroup/ns:BbdwEtlUserPassword'
    $value = Select-Xml -Path $localConfigFile -XPath $xPath -Namespace $ns |% { $_.Node.'#text' } | Select-Object -First 1

    return $value
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
		[string]$manifestFile,

        [parameter()]
        [string[]]$additionalCopyFolders
	)

	Log-Info "Preparing to copy customization files from '$sourceFolder' to CRM installation at '$crmInstallPath', writing copied files to '$manifestFile'"
		
	$folderMap = @{}

		
	# Configure web.config for bin\custom folder

	$ns = @{ns = 'urn:schemas-microsoft-com:asm.v1'}
	$webConfigPath = [System.IO.Path]::Combine($crmInstallPath, 'bbAppFx', 'vroot', 'web.config')
	$probeNode = Select-Xml -Path $webConfigPath -Namespace $ns -XPath "/configuration/runtime/ns:assemblyBinding/ns:probing"
	if (-not($probeNode))
	{
		Log-Info "Updating web.config to include bin\custom folder"
		[xml]$xml = Get-Content $WebConfigPath
		$newNode = $xml.CreateNode('element', 'probing', $ns['ns'])
		$newNodeAttr = $xml.CreateAttribute('privatePath')
		$newNodeAttr.Value = 'bin\custom'
		$newNode.ATtributes.Append($newNodeAttr)
		$xml.configuration.runtime.assemblyBinding.PrependChild($newNode)
		$xml.Save($WebConfigPath)
	}
		
	# BinDlls folder
	$src = [System.IO.Path]::Combine($sourceFolder, 'BinDlls')
	if (-not(Test-Path $src)) {
		throw "Did not find BinDlls folder at expected location: '$src'"
	}
    $folderMap.Add($src, @())

	$tgt = [System.IO.Path]::Combine($crmInstallPath, 'bbAppFx', 'vroot', 'bin', 'custom')
	$folderMap[$src] += $tgt

    # BinDlls copies to additional folders
    foreach ($item in $additionalCopyFolders)
    {
        $tgt = [System.IO.Path]::Combine($item, 'bin', 'custom')
	    $folderMap[$src] += $tgt
    }
    

	# HtmlForms folder
	$src = [System.IO.Path]::Combine($sourceFolder, 'HtmlForms')
	if (-not(Test-Path $src)) {
		throw "Did not find HtmlForms folder at expected location: '$src'"
	}
    $folderMap.Add($src, @())

	$tgt = [System.IO.Path]::Combine($crmInstallPath, 'bbAppFx', 'vroot', 'browser', 'htmlforms')
	$folderMap[$src] += $tgt

    # HtmlForms copies to additional folders
    foreach ($item in $additionalCopyFolders)
    {
        $tgt = [System.IO.Path]::Combine($item, 'browser', 'htmlforms')
	    $folderMap[$src] += $tgt
    }


	# RevisionDlls folder
	$src = [System.IO.Path]::Combine($sourceFolder, 'RevisionDlls')
	if (-not(Test-Path $src)) {
		throw "Did not find RevisionDlls folder at expected location: '$src'"
	}
    $folderMap.Add($src, @())
	$tgt = [System.IO.Path]::Combine($customInstallPath, 'RevisionDlls')
	$folderMap[$src] += $tgt

	# SQL folder
	$src = [System.IO.Path]::Combine($sourceFolder, 'Sql')
	if (-not(Test-Path $src)) {
		throw "Did not find SQL folder at expected location: '$src'"
	}
    $folderMap.Add($src, @())
	$tgt = [System.IO.Path]::Combine($customInstallPath, 'SQL')
	$folderMap[$src] += $tgt
	
	# System Roles folder
	$src = [System.IO.Path]::Combine($sourceFolder, 'SystemRoles')
	if (-not(Test-Path $src)) {
		throw "Did not find SystemRoles folder at expected location: '$src'"
	}
    $folderMap.Add($src, @())
	$tgt = [System.IO.Path]::Combine($customInstallPath, 'SystemRoles')
	$folderMap[$src] += $tgt
	
	# Tasks folder
	$src = [System.IO.Path]::Combine($sourceFolder, 'Tasks')
	if (-not(Test-Path $src)) {
		throw "Did not find Tasks folder at expected location: '$src'"
	}
    $folderMap.Add($src, @())
	$tgt = [System.IO.Path]::Combine($customInstallPath, 'Tasks')
	$folderMap[$src] += $tgt
    
	# BBDW extension folder
	if (-not($SkipBBDW)) 
	{
		$src = [System.IO.Path]::Combine($sourceFolder, 'BBDWExtension')
		if (-not(Test-Path $src)) {
			throw "Did not find Tasks folder at expected location: '$src'"
		}
		$folderMap.Add($src, @())
		$tgt = [System.IO.Path]::Combine($customInstallPath, 'BBDWExtension')
		$folderMap[$src] += $tgt
	}

	# Customization install support files
	$src = [System.IO.Path]::Combine($sourceFolder, 'InstallSupportFiles')
	if (-not(Test-Path $src)) {
		throw "Did not find install support files at expected location: '$src'"
	}
    $folderMap.Add($src, @())
    $folderMap[$src] += $customInstallPath

	# Do the copying, write each copied item to the manifest file
    New-Item $manifestFile -ItemType File -Force > $null

	$manifestDirectory = (Get-Item $manifestFile).Directory
	foreach ($map in $folderMap.GetEnumerator()) {
		$source = $map.Key
		#$target = $map.Value

        foreach ($target in $map.Value)
        {
		    $robocopyLogFile = [System.IO.Path]::Combine($manifestDirectory, 'robocopylog.txt')

		    Log-Info "Copying from '$source' to '$target', writing copied files to '$robocopyLogFile'" 
		    $counter = 0
				
		    robocopy $source $target /s /is /np /njh /njs /ns /nc /fp /xx /log:$robocopyLogFile *>> $LogFle

		    foreach ($line in (Get-Content $robocopyLogFile)) {
			    $updated = $line.Trim().Replace($source, $target)
			    if ($updated.Length -gt 0) {
				    Add-Content $manifestFile -Value $updated
				    $counter += 1
			    }
		    }

		    Log-Info "Copied $counter item(s)"
        }
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

    Log-Info "Running: $cmd"

	Invoke-Expression $cmd
}

function Run-MSBuildInstall {
	param(
		[parameter()]
		[string]$customInstallPath,
        [parameter()]
        [string]$bbdwEtlUserPassword
	)

	$msBuildLogPath = (Get-Item $LogFile).DirectoryName

	$msBuildPath = [System.IO.Path]::Combine($customInstallPath, 'CustomInstall.proj')
	if ($InstallWithRestore) {
		Log-Info "Running MSBuild with DB restore"
		$msBuildParams = "/t:InstallWithRestore"
	} else {
		$msBuildParams = "/t:Install"
	}

    if ($bbdwEtlUserPassword.Length -gt 0)
    {
        $msBuildParams = ('{0} /p:BbdwEtlUserPassword="{1}"' -f $msbuildParams,$bbdwEtlUserPassword)
    }

    $msBuildLogFile = $msBuildResult = Invoke-MsBuild -Path $msBuildPath -BuildLogDirectoryPath $msBuildLogPath -GetLogPath
	Log-Info "Preparing to invoke MSBuild, log will be written to '$msBuildLogFile' and, upon completion, appended to '$LogFile'"

	$msBuildResult = $True
	if ($NonInteractive) {
		$msBuildResult = Invoke-MsBuild -Path $msBuildPath -MsBuildParameters $msBuildParams -BuildLogDirectoryPath $msBuildLogPath -KeepBuildLogOnSuccessfulBuilds
	} else {
		$msBuildResult = Invoke-MsBuild -Path $msBuildPath -MsBuildParameters $msBuildParams -BuildLogDirectoryPath $msBuildLogPath -ShowBuildWindowAndPromptForInputBeforeClosing -KeepBuildLogOnSuccessfulBuilds
	}

  if (Test-Path $msBuildLogFile) {
    Log-Info "Appending '$msBuildLogFile' to '$LogFile'"
    Add-Content $LogFile -Value (Get-Content $msBuildLogFile)
    Remove-Item $msBuildLogFile
  }

	if (-not $msBuildResult) {
		throw "MSBuild failed"
	}
}


# Start script
$msg = "********** START INSTALLATION **********"
Log-Info $msg

#if (-NOT ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator"))
#{
#    $msg = "Not running as administrator."
#    Log-Error $msg
#    Exit 1
#}

Write-Host "Logging to $LogFile"

Add-Type -Assembly "System.IO.Compression.FileSystem"

Import-Module -Name ([System.IO.Path]::Combine($PSScriptRoot, "Invoke-MsBuild.psm1"))

Unblock-File -Path "$PSScriptRoot\**\*"

try
{
	$installPath = Get-CrmInstallPath $InstanceName
	if ($installPath.Length -eq 0) {
		throw "Failed to find install path for CRM instance '$InstanceName'"
	}

	$manifestFile = [System.IO.Path]::Combine($PSScriptRoot, 'BBCRMCustomInstallManifest.txt')

	$customInstallPath = [System.IO.Path]::Combine($installPath, 'bbAppFx', 'MSBuild', 'Custom')
	
	$msg = "Using custom install path: '$customInstallPath'"
	Log-Info $msg
	Write-Host $msg
    
    # Look for <CopyToVroot> items in LocalConfig.xml, indicating additional folders we need to copy to
    $additionalCopyFolders = New-Object string[] 0

    # If custom install path is present, look for LocalConfig.xml to retrieve environment-specific settings used for deployment
    if (Test-Path $customInstallPath)
    {
        $localConfigFile = [System.IO.Path]::Combine($customInstallPath, 'LocalConfig.xml')

        # If we're not skipping BBDW deployment and there is a config file present, try to get a BBDW ETL user password from it
        if (-Not($SkipBBDW) -and (Test-Path $localConfigFile))
        {
            $BBDWUserPassword = Get-BbdwPasswordFromConfig $localConfigFile
	}

        # Prompt for BBDW user password if we are NOT skipping BBDW and we are NOT running a no-install deploy, but we have not been supplied with a password
        $BBDWUserPasswordSecure = ""
        if (-Not($SkipBBDW) -and ($BBDWUserPassword.Length -eq 0) -and -not($NoInstall))
        {
            $BBDWUserPasswordSecure = Read-Host "What is the BBDW ETL user password?" -AsSecureString 
            $BBDWUserPassword = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($BBDWUserPasswordSecure))
        }

        # Look for additional vroot folders we need to copy to (i.e. load-balanced servers)
    if (Test-Path $localConfigFile)
    {
        $additionalCopyFolders = Get-AdditionalCopyFolders $localConfigFile
        Log-Info "Setting additional copy folders: $additionalCopyFolders"
    }
    }
   
	if (-not(Test-Path $customInstallPath)) {
		New-Item $customInstallPath -Type Directory > $null
	} else {
		Uninstall-CustomizationFiles $customInstallPath $manifestFile
	}

	Copy-CustomizationFiles $PSScriptRoot $installPath $customInstallPath $manifestFile $additionalCopyFolders

	if ($NoInstall) {
		Log-Info "NoInstall command line parameter set, skipping installation."
	} else {
		Run-MSBuildInstall $customInstallPath $BBDWUserPassword
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
	Log-Info "********** END INSTALLATION **********"
	Exit 0
}