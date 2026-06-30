param(
    [string]$BaseUrl = "http://localhost:5000",
    [string]$TestEmail = "openjibo-smoke@example.com",
    [string]$TestPassword = "OpenJiboSmokePass!42",
    [string]$TestFirstName = "Open",
    [string]$TestLastName = "Jibo",
    [string]$TestRobotId = "open-jibo-smoke-robot"
)

$ErrorActionPreference = "Stop"
$baseHost = ([System.Uri]::new($BaseUrl)).Authority

function Invoke-JsonRequest {
    param(
        [string]$Name,
        [string]$Method,
        [string]$Url,
        [hashtable]$Headers,
        [string]$Body
    )

    $request = @{
        Uri = $Url
        Method = $Method
        Headers = $Headers
        ErrorAction = "Stop"
    }

    if ($null -ne $Body) {
        $request.Body = $Body
        $request.ContentType = "application/json"
    }

    try {
        $response = Invoke-WebRequest @request
        $responseBody = $response.Content
        $parsedBody = $null
        if (-not [string]::IsNullOrWhiteSpace($responseBody)) {
            try {
                $parsedBody = $responseBody | ConvertFrom-Json
            } catch {
                $parsedBody = $responseBody
            }
        }

        return [pscustomobject]@{
            Name = $Name
            Success = $true
            StatusCode = $response.StatusCode
            Body = $parsedBody
            BodyText = $responseBody
        }
    } catch {
        $statusCode = $null
        $bodyText = $null

        if ($_.Exception.Response) {
            if ($_.Exception.Response.StatusCode) {
                $statusCode = [int]$_.Exception.Response.StatusCode
            }

            try {
                $stream = $_.Exception.Response.GetResponseStream()
                if ($stream) {
                    $reader = [System.IO.StreamReader]::new($stream)
                    $bodyText = $reader.ReadToEnd()
                    $reader.Dispose()
                }
            } catch {
                $bodyText = $null
            }
        }

        return [pscustomobject]@{
            Name = $Name
            Success = $false
            StatusCode = $statusCode
            Body = $bodyText
            BodyText = $bodyText
            Error = $_.Exception.Message
        }
    }
}

function Invoke-JsonRequestWithRetry {
    param(
        [string]$Name,
        [string]$Method,
        [string]$Url,
        [hashtable]$Headers,
        [string]$Body,
        [int]$Attempts = 4,
        [int[]]$RetryStatusCodes = @(500, 502, 503, 504)
    )

    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        $result = Invoke-JsonRequest -Name $Name -Method $Method -Url $Url -Headers $Headers -Body $Body
        if ($result.Success -or ($RetryStatusCodes -notcontains $result.StatusCode) -or ($attempt -eq $Attempts)) {
            return $result
        }

        Write-Warning "$Name attempt $attempt failed with $($result.StatusCode); retrying after a short delay."
        Start-Sleep -Seconds ([Math]::Min(5, $attempt * 2))
    }
}

$results = New-Object System.Collections.Generic.List[object]

function Add-Result {
    param([object]$Result)
    $results.Add($Result) | Out-Null
    $Result
}

Add-Result (Invoke-JsonRequest -Name "Health" -Method "GET" -Url "$BaseUrl/health" -Headers @{})

$createBody = @{ email = $TestEmail; password = $TestPassword; firstName = $TestFirstName; lastName = $TestLastName } | ConvertTo-Json
$account = Invoke-JsonRequestWithRetry -Name "AccountCreate" -Method "POST" -Url "$BaseUrl/" -Headers @{
    "X-Amz-Target" = "Account_20151111.Create"
    Host = $baseHost
} -Body $createBody
Add-Result $account

if (-not $account.Success -and $account.StatusCode -ne 409) {
    throw "Account create failed with status code $($account.StatusCode): $($account.Error). Body: $($account.BodyText ?? $account.Body)"
}

if (-not $account.Success) {
    $login = Invoke-JsonRequest -Name "AccountLogin" -Method "POST" -Url "$BaseUrl/" -Headers @{
        "X-Amz-Target" = "Account_20151111.Login"
        Host = $baseHost
    } -Body (@{ email = $TestEmail; password = $TestPassword } | ConvertTo-Json)
    Add-Result $login
    if (-not $login.Success) {
        throw "Account login failed with status code $($login.StatusCode): $($login.Error). Body: $($login.BodyText ?? $login.Body)"
    }
    $account = $login
}

$accountId = $null
if ($account.Body -and $account.Body.PSObject.Properties.Name -contains "id") {
    $accountId = [string]$account.Body.id
}

$loops = Invoke-JsonRequest -Name "LoopList" -Method "POST" -Url "$BaseUrl/" -Headers @{
    "X-Amz-Target" = "Loop_20160324.ListLoops"
    Host = $baseHost
} -Body "{}"
Add-Result $loops

if (-not $loops.Success) {
    throw "Loop list failed with status code $($loops.StatusCode): $($loops.Error)"
}

$loopId = "openjibo-default-loop"
if ($loops.Body -is [System.Array] -and $loops.Body.Count -gt 0 -and $loops.Body[0].PSObject.Properties.Name -contains "id") {
    $loopId = [string]$loops.Body[0].id
}

$members = Invoke-JsonRequest -Name "LoopListMembers" -Method "POST" -Url "$BaseUrl/" -Headers @{
    "X-Amz-Target" = "Loop_20160324.ListMembers"
    Host = $baseHost
} -Body (@{ loopId = $loopId } | ConvertTo-Json)
Add-Result $members

if (-not $members.Success) {
    throw "Loop members failed with status code $($members.StatusCode): $($members.Error)"
}

$prepareBody = @{ loopId = $loopId; rollbackSnapshotId = "smoke-rollback-$TestRobotId" }
if ($accountId) {
    $prepareBody.accountId = $accountId
}

$prepare = Invoke-JsonRequest -Name "PrepareRobot" -Method "POST" -Url "$BaseUrl/" -Headers @{
    "X-Amz-Target" = "OOBE_20161026.PrepareRobot"
    Host = $baseHost
} -Body ($prepareBody | ConvertTo-Json)
Add-Result $prepare

if (-not $prepare.Success) {
    throw "PrepareRobot failed with status code $($prepare.StatusCode): $($prepare.Error)"
}

$token = $null
if ($prepare.Body -and $prepare.Body.PSObject.Properties.Name -contains "token") {
    $token = [string]$prepare.Body.token
}

if ([string]::IsNullOrWhiteSpace($token)) {
    throw "PrepareRobot did not return a token."
}

$statusBefore = Invoke-JsonRequest -Name "GetStatusBeforeSetup" -Method "POST" -Url "$BaseUrl/" -Headers @{
    "X-Amz-Target" = "OOBE_20161026.GetStatus"
    Host = $baseHost
} -Body (@{ token = $token } | ConvertTo-Json)
Add-Result $statusBefore

if (-not $statusBefore.Success) {
    throw "GetStatus before setup failed with status code $($statusBefore.StatusCode): $($statusBefore.Error)"
}

$setupBody = @{ token = $token; id = $TestRobotId } | ConvertTo-Json
$setup = Invoke-JsonRequest -Name "SetupRobot" -Method "POST" -Url "$BaseUrl/" -Headers @{
    "X-Amz-Target" = "OOBE_20161026.SetupRobot"
    Host = $baseHost
} -Body $setupBody
Add-Result $setup

if (-not $setup.Success) {
    throw "SetupRobot failed with status code $($setup.StatusCode): $($setup.Error)"
}

$statusAfter = Invoke-JsonRequest -Name "GetStatusAfterSetup" -Method "POST" -Url "$BaseUrl/" -Headers @{
    "X-Amz-Target" = "OOBE_20161026.GetStatus"
    Host = $baseHost
} -Body (@{ token = $token } | ConvertTo-Json)
Add-Result $statusAfter

if (-not $statusAfter.Success) {
    throw "GetStatus after setup failed with status code $($statusAfter.StatusCode): $($statusAfter.Error)"
}

$results
