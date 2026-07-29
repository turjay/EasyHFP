<#
.SYNOPSIS
    EasyHFP - Windows Bluetooth Hands-Free Telephony Toggle Script
.DESCRIPTION
    Configuration-based Bluetooth Headset Hands-Free Telephony (0000111e-0000-1000-8000-00805f9b34fb) Toggle Tool.
#>

[CmdletBinding()]
param(
    [Alias("s")]
    [switch]$Setup
)

$Host.UI.RawUI.WindowTitle = "EasyHFP - Bluetooth Audio Manager"

# File paths
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
if (-not $ScriptDir) { $ScriptDir = Get-Location }
$ConfigFile = Join-Path -Path $ScriptDir -ChildPath "config.txt"

# Console Logging Helper Functions (using PowerShell approved verb 'Write')
function Write-HfpInfo {
    param ([string]$Message)
    $ts = Get-Date -Format "HH:mm:ss"
    Write-Host "[$ts] " -NoNewline -ForegroundColor DarkGray
    Write-Host "[INFO] " -NoNewline -ForegroundColor Cyan
    Write-Host $Message -ForegroundColor White
}

function Write-HfpReconnect {
    param ([string]$Message)
    $ts = Get-Date -Format "HH:mm:ss"
    Write-Host "[$ts] " -NoNewline -ForegroundColor DarkGray
    Write-Host "[RECONNECT] " -NoNewline -ForegroundColor Yellow
    Write-Host $Message -ForegroundColor Yellow
}

function Write-HfpSuccess {
    param ([string]$Message)
    $ts = Get-Date -Format "HH:mm:ss"
    Write-Host "[$ts] " -NoNewline -ForegroundColor DarkGray
    Write-Host "[SUCCESS] " -NoNewline -ForegroundColor Green
    Write-Host $Message -ForegroundColor Green
}

function Write-HfpError {
    param ([string]$Message)
    $ts = Get-Date -Format "HH:mm:ss"
    Write-Host "[$ts] " -NoNewline -ForegroundColor DarkGray
    Write-Host "[ERROR] " -NoNewline -ForegroundColor Red
    Write-Host $Message -ForegroundColor Red
}

# Administrator Privilege Check
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
$isAdmin = $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Warning "WARNING: Running as Administrator is recommended for automatic PnP device reconnect."
}

# Win32 Bluetooth API Definitions
$code = @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace EasyHFP
{
    [StructLayout(LayoutKind.Sequential)]
    public struct BLUETOOTH_ADDRESS { public ulong ullLong; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct BLUETOOTH_DEVICE_INFO
    {
        public uint dwSize;
        public BLUETOOTH_ADDRESS Address;
        public uint ulClassofDevice;
        public bool fConnected;
        public bool fRemembered;
        public bool fAuthenticated;
        public SYSTEMTIME stLastSeen;
        public SYSTEMTIME stLastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)]
        public string szName;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEMTIME { public ushort wYear, wMonth, wDayOfWeek, wDay, wHour, wMinute, wSecond, wMilliseconds; }

    [StructLayout(LayoutKind.Sequential)]
    public struct BLUETOOTH_DEVICE_SEARCH_PARAMS
    {
        public uint dwSize;
        public bool fReturnAuthenticated;
        public bool fReturnRemembered;
        public bool fReturnUnknown;
        public bool fReturnConnected;
        public bool fIssueInquiry;
        public byte cTimeoutMultiplier;
        public IntPtr hRadio;
    }

    public class BtManager
    {
        [DllImport("bthprops.cpl", SetLastError = true)]
        public static extern IntPtr BluetoothFindFirstDevice(ref BLUETOOTH_DEVICE_SEARCH_PARAMS pbtsp, ref BLUETOOTH_DEVICE_INFO pbtdi);

        [DllImport("bthprops.cpl", SetLastError = true)]
        public static extern bool BluetoothFindNextDevice(IntPtr hFind, ref BLUETOOTH_DEVICE_INFO pbtdi);

        [DllImport("bthprops.cpl", SetLastError = true)]
        public static extern bool BluetoothFindDeviceClose(IntPtr hFind);

        [DllImport("bthprops.cpl", SetLastError = true)]
        public static extern uint BluetoothSetServiceState(IntPtr hRadio, ref BLUETOOTH_DEVICE_INFO pbtdi, ref Guid pGuidService, uint dwFlags);

        [DllImport("bthprops.cpl", SetLastError = true)]
        public static extern uint BluetoothEnumerateInstalledServices(IntPtr hRadio, ref BLUETOOTH_DEVICE_INFO pbtdi, ref uint pcServices, [In, Out] Guid[] pGuidServices);
    }
}
"@

if (-not ([System.Management.Automation.PSTypeName]'EasyHFP.BtManager').Type) {
    Add-Type -TypeDefinition $code
}

function Show-ToastNotification {
    param ([string]$Title, [string]$Message)
    try {
        $null = [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]
        $null = [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime]
        $toastXml = @"
<toast>
    <visual>
        <binding template="ToastGeneric">
            <text>$Title</text>
            <text>$Message</text>
        </binding>
    </visual>
</toast>
"@
        $xml = New-Object Windows.Data.Xml.Dom.XmlDocument
        $xml.LoadXml($toastXml)
        $toast = [Windows.UI.Notifications.ToastNotification]::new($xml)
        $notifier = [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier("EasyHFP Bluetooth Manager")
        $notifier.Show($toast)
    } catch {
        Add-Type -AssemblyName System.Windows.Forms -ErrorAction SilentlyContinue
        $notify = New-Object System.Windows.Forms.NotifyIcon
        $notify.Icon = [System.Drawing.SystemIcons]::Information
        $notify.Visible = $true
        $notify.ShowBalloonTip(3000, $Title, $Message, [System.Windows.Forms.ToolTipIcon]::Info)
        Start-Sleep -Seconds 1
        $notify.Dispose()
    }
}

function Get-BluetoothDevices {
    $searchParams = New-Object EasyHFP.BLUETOOTH_DEVICE_SEARCH_PARAMS
    $searchParams.dwSize = [System.Runtime.InteropServices.Marshal]::SizeOf($searchParams)
    $searchParams.fReturnRemembered = $true
    $searchParams.fReturnConnected = $true

    $deviceInfo = New-Object EasyHFP.BLUETOOTH_DEVICE_INFO
    $deviceInfo.dwSize = [System.Runtime.InteropServices.Marshal]::SizeOf($deviceInfo)

    $hFind = [EasyHFP.BtManager]::BluetoothFindFirstDevice([ref]$searchParams, [ref]$deviceInfo)
    $list = @()

    if ($hFind -ne [IntPtr]::Zero) {
        do {
            $list += [PSCustomObject]@{
                Name      = $deviceInfo.szName
                MAC       = $deviceInfo.Address.ullLong.ToString("X12")
                MacUlong  = $deviceInfo.Address.ullLong
                Connected = $deviceInfo.fConnected
                RawInfo   = $deviceInfo
            }
        } while ([EasyHFP.BtManager]::BluetoothFindNextDevice($hFind, [ref]$deviceInfo))
        [EasyHFP.BtManager]::BluetoothFindDeviceClose($hFind)
    }
    return $list
}

if ($Setup -or (Test-Path -Path $ConfigFile) -and ($args -contains "--setup" -or $args -contains "-s")) {
    if (Test-Path -Path $ConfigFile) {
        Remove-Item -Path $ConfigFile -Force -ErrorAction SilentlyContinue
        Write-HfpInfo "Existing config.txt removed. Launching setup mode...`n"
    }
}

if (-not (Test-Path -Path $ConfigFile)) {
    Write-Host "=============================================" -ForegroundColor Cyan
    Write-Host "     EasyHFP - Bluetooth Headset Setup       " -ForegroundColor Yellow
    Write-Host "=============================================" -ForegroundColor Cyan
    Write-HfpInfo "Scanning for paired Bluetooth devices...`n"

    $devices = Get-BluetoothDevices

    if ($devices.Count -eq 0) {
        Write-HfpError "No paired Bluetooth devices found!"
        Read-Host "Press Enter to exit..."
        exit 1
    }

    for ($i = 0; $i -lt $devices.Count; $i++) {
        $dev = $devices[$i]
        $statusStr = if ($dev.Connected) { "Connected" } else { "Disconnected" }
        Write-Host ("[{0}] {1} (MAC: {2}) - Status: {3}" -f ($i + 1), $dev.Name, $dev.MAC, $statusStr) -ForegroundColor Green
    }

    Write-Host ""
    $selection = 0
    while ($selection -lt 1 -or $selection -gt $devices.Count) {
        $inputStr = Read-Host ("Please select your Bluetooth headset number (1-{0})" -f $devices.Count)
        [int]::TryParse($inputStr, [ref]$selection) | Out-Null
    }

    $selectedDev = $devices[$selection - 1]
    
    $configContent = "MAC=$($selectedDev.MAC)`nNAME=$($selectedDev.Name)"
    Set-Content -Path $ConfigFile -Value $configContent -Encoding UTF8

    Write-HfpSuccess ("Selected Device: {0} (MAC: {1})" -f $selectedDev.Name, $selectedDev.MAC)
    Write-HfpInfo ("Configuration file saved: {0}" -f $ConfigFile)
    Write-HfpInfo "EasyHFP will now automatically target this device on future runs.`n"
    
    Show-ToastNotification -Title "EasyHFP Setup Complete" -Message "Default headset saved: $($selectedDev.Name)"
    Start-Sleep -Seconds 2
    exit 0
}

Write-HfpInfo "Loading configuration file..."
$configLines = Get-Content -Path $ConfigFile -ErrorAction SilentlyContinue
$targetMac = ""
$targetName = "Bluetooth Headset"

foreach ($line in $configLines) {
    if ($line -like "MAC=*") { $targetMac = $line.Substring(4).Trim().Replace(":", "").Replace("-", "") }
    if ($line -like "NAME=*") { $targetName = $line.Substring(5).Trim() }
}

if ([string]::IsNullOrWhiteSpace($targetMac)) {
    Write-HfpError "No valid MAC address found in config.txt. Launching setup mode..."
    Remove-Item -Path $ConfigFile -Force -ErrorAction SilentlyContinue
    & $MyInvocation.MyCommand.Path --setup
    exit 1
}

Write-HfpInfo ("Target headset: {0} (MAC: {1})" -f $targetName, $targetMac)
Write-HfpInfo "Scanning Bluetooth devices..."

$devices = Get-BluetoothDevices
$targetDev = $devices | Where-Object { $_.MAC -ieq $targetMac }

if ($null -eq $targetDev) {
    $msg = "Saved Bluetooth headset not found! MAC: '$targetMac'"
    Write-HfpError $msg
    Show-ToastNotification -Title "EasyHFP Error" -Message $msg
    Start-Sleep -Seconds 2
    exit 1
}

$deviceInfo = $targetDev.RawInfo
$hfpGuid = [Guid]::Parse("0000111e-0000-1000-8000-00805f9b34fb")

Write-HfpInfo "Checking Hands-Free Telephony service status..."
[uint32]$pcServices = 0
[EasyHFP.BtManager]::BluetoothEnumerateInstalledServices([IntPtr]::Zero, [ref]$deviceInfo, [ref]$pcServices, $null)
$guids = New-Object Guid[] $pcServices
if ($pcServices -gt 0) { [EasyHFP.BtManager]::BluetoothEnumerateInstalledServices([IntPtr]::Zero, [ref]$deviceInfo, [ref]$pcServices, $guids) }

$isHfpActive = $guids -contains $hfpGuid
$targetState = if ($isHfpActive) { 0 } else { 1 }
$targetModeStr = if ($targetState -eq 0) { "DISABLED (A2DP Stereo Mode)" } else { "ENABLED (Microphone & Call Mode)" }

Write-HfpInfo ("Current HFP Status: {0} -> Switching to: {1}" -f (if ($isHfpActive) { "ENABLED" } else { "DISABLED" }), $targetModeStr)

Write-HfpInfo "Toggling Hands-Free service state via Win32 API..."
$res = [EasyHFP.BtManager]::BluetoothSetServiceState([IntPtr]::Zero, [ref]$deviceInfo, [ref]$hfpGuid, $targetState)

if ($res -ne 0) {
    $errorMsg = "BluetoothSetServiceState failed with error code: $res"
    Write-HfpError $errorMsg
    Show-ToastNotification -Title "EasyHFP Error" -Message $errorMsg
    Start-Sleep -Seconds 2
    exit 1
}

# --- ASYMMETRIC RECONNECT LOGIC ---
if ($isAdmin) {
    if ($targetState -eq 1) {
        # HFP ENABLING (A2DP -> HFP Call Mode) - UNTOUCHED (Working Perfectly)
        Write-HfpReconnect "Initiating PnP reconnect for headset (HFP Call Mode)..."
        Write-HfpReconnect "Disabling headset PnP node..."
        $pnpDevices = Get-PnpDevice | Where-Object { $_.InstanceId -like "BTHENUM\DEV_$targetMac*" }
        foreach ($pnp in $pnpDevices) { Disable-PnpDevice -InstanceId $pnp.InstanceId -Confirm:$false -ErrorAction SilentlyContinue }
        
        Write-HfpReconnect "Waiting 3.5s for handles to reset..."
        Start-Sleep -Seconds 3.5
        
        Write-HfpReconnect "Re-enabling headset PnP node..."
        foreach ($pnp in $pnpDevices) { Enable-PnpDevice -InstanceId $pnp.InstanceId -Confirm:$false -ErrorAction SilentlyContinue }
        Start-Sleep -Seconds 1
    } else {
        # HFP DISABLING (HFP Call Mode -> A2DP Stereo High Quality)
        # WinRT Master Bluetooth Radio Soft Toggle (Exact Windows Action Center Bluetooth Button behavior)
        Write-HfpReconnect "Toggling Windows Master Bluetooth Radio OFF (Soft Disconnect via WinRT API)..."
        
        $exePath = Join-Path -Path $ScriptDir -ChildPath "EasyHFP.exe"
        if (Test-Path $exePath) {
            & $exePath --radio-toggle-only
        } else {
            Write-HfpReconnect "Waiting 3.0s for Bluetooth Radio to gracefully close L2CAP channels..."
            Start-Sleep -Seconds 3
        }
    }
}

$successMsg = if ($targetState -eq 0) { "Hands-Free Telephony DISABLED for $targetName (A2DP Stereo Active)!" } else { "Hands-Free Telephony ENABLED for $targetName (Call Mode Active)!" }
Write-HfpSuccess $successMsg

if ($targetState -eq 0) {
    $toastTitle = "Hands-Free Telephony: DISABLED 🎧"
    $toastBody = "Hands-Free Telephony disabled for $targetName. A2DP high-quality stereo audio active!"
} else {
    $toastTitle = "Hands-Free Telephony: ENABLED 🎙️"
    $toastBody = "Hands-Free Telephony enabled for $targetName. Microphone & hands-free call mode active!"
}

Show-ToastNotification -Title $toastTitle -Message $toastBody

Write-HfpInfo "Closing window in 2 seconds..."
Start-Sleep -Seconds 2
