param(
    [string]$TargetIp,
    [string[]]$HostNames = @(
        "api.jibo.com",
        "api-socket.jibo.com",
        "open-jibo-socket.openjibo.com",
        "neo-hub.jibo.com",
        "neohub.openjibo.com"
    )
)

if ([string]::IsNullOrWhiteSpace($TargetIp)) {
    throw "TargetIp is required."
}

$entries = foreach ($host in $HostNames) {
    [pscustomobject]@{
        Host = $host
        TargetIp = $TargetIp
        HostsFileLine = "$TargetIp`t$host"
    }
}

$entries | Format-Table -AutoSize
