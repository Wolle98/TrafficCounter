<#
.SYNOPSIS
    Installs the TrafficCounter package as a Windows service (ADR-0012). Run as administrator on the target machine.

.DESCRIPTION
    - copies the package to the install directory (default C:\Program Files\TrafficCounter),
    - sets TRAFFICCOUNTER_CONFIG as a system variable (a service does not see user variables),
    - gives the service account access to SQL Server (login + dbcreator; the service creates and migrates the
      database itself),
    - creates the service "TrafficCounter": automatic (delayed) start, restart after a crash, event log source,
    - optionally disables sleep on AC power, so the counter keeps running.
    Logs and the database fallback file go to %ProgramData%\TrafficCounter.

    The service runs as "Local System" by default. If the USB camera cannot be opened from a service (check the log
    or the SourceConnected event), install with -Account and the credentials of the logged-on user instead.

.EXAMPLE
    .\Install-TrafficCounter.ps1 -SiteConfig C:\TrafficCounter\config\site.json

.EXAMPLE
    .\Install-TrafficCounter.ps1 -SiteConfig C:\TrafficCounter\config\site.json -Account "$env:COMPUTERNAME\$env:USERNAME" -DisableSleepOnAc
#>
#Requires -RunAsAdministrator
param(
    # Private site configuration (calibration, direction names, coordinates); site.drift.json next to it is used too.
    [Parameter(Mandatory)]
    [string] $SiteConfig,

    [string] $InstallDir = (Join-Path $env:ProgramFiles 'TrafficCounter'),

    # SqlServer (local, database created by the service) or PostgreSql (e.g. on the NAS, set up with
    # TrafficCounter.Sync setup-postgres, ADR-0030).
    [ValidateSet('SqlServer', 'PostgreSql')]
    [string] $DatabaseProvider = 'SqlServer',

    # SQL Server instance; the database "TrafficCounter" is created by the service.
    [string] $SqlServer = 'localhost',

    # PostgreSQL connection without password, e.g. "Host=nas;Port=5432;Database=trafficcounter;Username=trafficcounter".
    # The password is asked for interactively.
    [string] $PostgresConnection = '',

    # Windows account for the service, e.g. "NOTEBOOK\User". Empty: Local System.
    [string] $Account = '',

    # Model file in <InstallDir>\models. Set as a system variable because it must win over a ModelPath in the
    # private site configuration (which may point to the development machine).
    [string] $Model = 'yolo11s-640x192.onnx',

    [switch] $DisableSleepOnAc
)

$ErrorActionPreference = 'Stop'
$serviceName = 'TrafficCounter'
$package = Join-Path $PSScriptRoot 'app'

if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Run this script in Windows PowerShell 5.1 (powershell.exe), not PowerShell 7.' }

if (-not (Test-Path (Join-Path $package 'TrafficCounter.exe'))) { throw "No package in $package - run Publish-TrafficCounter.ps1 first." }
$SiteConfig = (Resolve-Path $SiteConfig).Path

# 1. Stop a running installation, copy the files.
if (Get-Service $serviceName -ErrorAction SilentlyContinue) {
    Write-Host "Stopping existing service"
    Stop-Service $serviceName -Force -ErrorAction SilentlyContinue
}

Write-Host "Copying to $InstallDir"
New-Item -ItemType Directory -Force $InstallDir | Out-Null
Copy-Item (Join-Path $package '*') $InstallDir -Recurse -Force

# 2. Configuration for the service account.
[Environment]::SetEnvironmentVariable('TRAFFICCOUNTER_CONFIG', $SiteConfig, 'Machine')
[Environment]::SetEnvironmentVariable('TrafficCounter__Storage__Provider', $DatabaseProvider, 'Machine')
if ($DatabaseProvider -eq 'PostgreSql') {
    if (-not $PostgresConnection) { throw '-PostgresConnection is required for PostgreSql.' }
    # The password stays out of the connection string, in its own system variable (set it before, or type it now).
    if (-not [Environment]::GetEnvironmentVariable('TRAFFICCOUNTER_PG_PASSWORD', 'Machine')) {
        $secure = Read-Host 'Password of the PostgreSQL role' -AsSecureString
        $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
        [Environment]::SetEnvironmentVariable('TRAFFICCOUNTER_PG_PASSWORD', $password, 'Machine')
    }
    [Environment]::SetEnvironmentVariable('ConnectionStrings__TrafficCounter', $PostgresConnection, 'Machine')
}
else {
    [Environment]::SetEnvironmentVariable(
        'ConnectionStrings__TrafficCounter',
        "Server=$SqlServer;Database=TrafficCounter;Integrated Security=true;TrustServerCertificate=true",
        'Machine')
}
$modelPath = Join-Path $InstallDir "models\$Model"
if (-not (Test-Path $modelPath)) { throw "Model $modelPath not found; include it with Publish-TrafficCounter.ps1 -Models." }
[Environment]::SetEnvironmentVariable('TrafficCounter__Detector__ModelPath', $modelPath, 'Machine')
Write-Host "System variables TRAFFICCOUNTER_CONFIG, ConnectionStrings__TrafficCounter and TrafficCounter__Detector__ModelPath set"

# 3. SQL Server access for the service account (PostgreSQL: the role was set up with TrafficCounter.Sync).
$sqlLogin = if ($Account) { $Account } else { 'NT AUTHORITY\SYSTEM' }
$connection = New-Object System.Data.SqlClient.SqlConnection "Server=$SqlServer;Database=master;Integrated Security=true;TrustServerCertificate=true"
if ($DatabaseProvider -eq 'SqlServer') { try {
    $connection.Open()
    $command = $connection.CreateCommand()
    $command.CommandText = @"
IF SUSER_ID(N'$sqlLogin') IS NULL CREATE LOGIN [$sqlLogin] FROM WINDOWS;
IF IS_SRVROLEMEMBER('dbcreator', N'$sqlLogin') = 0 ALTER SERVER ROLE dbcreator ADD MEMBER [$sqlLogin];
IF DB_ID('TrafficCounter') IS NOT NULL
BEGIN
    EXEC('USE [TrafficCounter];
          IF USER_ID(N''$sqlLogin'') IS NULL CREATE USER [$sqlLogin] FOR LOGIN [$sqlLogin];
          ALTER ROLE db_owner ADD MEMBER [$sqlLogin];');
END
"@
    $command.ExecuteNonQuery() | Out-Null
    Write-Host "SQL Server: login $sqlLogin can create and use the database"
}
finally {
    $connection.Dispose()
} }

# 4. Event log source (needs administrator rights, so create it here rather than at the first start).
if (-not [Diagnostics.EventLog]::SourceExists($serviceName)) {
    New-EventLog -LogName Application -Source $serviceName
}

# 5. The service.
$exe = Join-Path $InstallDir 'TrafficCounter.exe'
# Start after a local SQL Server: default instance "MSSQLSERVER", named instance (e.g. localhost\SQLEXPRESS) "MSSQL$SQLEXPRESS".
$sqlHost, $sqlInstance = $SqlServer -split '\\', 2
$sqlService = if ($sqlInstance) { "MSSQL`$$sqlInstance" } else { 'MSSQLSERVER' }
$dependency = if ($DatabaseProvider -eq 'SqlServer' -and $sqlHost -in @('localhost', '.', '(local)', $env:COMPUTERNAME) -and (Get-Service $sqlService -ErrorAction SilentlyContinue)) { $sqlService } else { '' }
if (-not (Get-Service $serviceName -ErrorAction SilentlyContinue)) {
    $arguments = @('create', $serviceName, 'binPath=', "`"$exe`"", 'start=', 'delayed-auto', 'DisplayName=', 'TrafficCounter')
    if ($dependency) { $arguments += @('depend=', $dependency) }
    & sc.exe @arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "sc.exe create failed ($LASTEXITCODE)" }
}

& sc.exe description $serviceName 'Counts road traffic with a camera (TrafficCounter).' | Out-Null
& sc.exe failure $serviceName reset= 86400 actions= restart/60000/restart/60000/restart/300000 | Out-Null

if ($Account) {
    $credential = Get-Credential -UserName $Account -Message "Password of $Account for the TrafficCounter service"
    & sc.exe config $serviceName obj= $Account password= $credential.GetNetworkCredential().Password | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "sc.exe config (account) failed ($LASTEXITCODE)" }
}

# 6. Keep the machine awake on AC power (optional).
if ($DisableSleepOnAc) {
    & powercfg /change standby-timeout-ac 0
    & powercfg /change hibernate-timeout-ac 0
    Write-Host "Sleep and hibernation on AC power disabled"
}

Start-Service $serviceName
Start-Sleep -Seconds 20
Get-Service $serviceName | Format-Table Name, Status, StartType -AutoSize

$log = Get-ChildItem (Join-Path $env:ProgramData 'TrafficCounter\logs') -Filter 'trafficcounter-*.log' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime | Select-Object -Last 1
if ($log) {
    Write-Host "Last log lines ($($log.FullName)):"
    Get-Content $log.FullName -Tail 15
}
Write-Host ""
Write-Host "Check that the log shows 'USB camera ... opened' and that minute lines appear."
Write-Host "If the camera cannot be opened as Local System, reinstall with -Account."
