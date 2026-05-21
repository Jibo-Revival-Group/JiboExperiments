using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Domain.Models;
using Microsoft.AspNetCore.Mvc;

namespace Jibo.Cloud.Api.Controllers;

[ApiController]
[Route("api/panel")]
public class WebPanelController(
    ICloudStateStore stateStore,
    IConfiguration configuration) : ControllerBase
{
    private static readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;

    [HttpGet("status")]
    public ActionResult GetStatus()
    {
        var persistenceInfo = stateStore.GetPersistenceStateInfo();
        var account = stateStore.GetAccount();
        var robot = stateStore.GetRobot();

        return Ok(new
        {
            version = OpenJiboCloudBuildInfo.Version,
            uptime = (DateTimeOffset.UtcNow - _startTime).ToString(@"hh\:mm\:ss"),
            startTime = _startTime.ToString("o"),
            persistence = new
            {
                schemaVersion = persistenceInfo.SchemaVersion,
                revision = persistenceInfo.Revision,
                lastLoaded = persistenceInfo.LastLoadedUtc?.ToString("o"),
                lastSaved = persistenceInfo.LastSavedUtc?.ToString("o")
            },
            account = new
            {
                accountId = account.AccountId,
                firstName = account.FirstName,
                lastName = account.LastName
            },
            robot = new
            {
                deviceId = robot.DeviceId,
                robotId = robot.RobotId,
                friendlyName = robot.FriendlyName,
                firmwareVersion = robot.FirmwareVersion,
                applicationVersion = robot.ApplicationVersion
            },
            configuration = new
            {
                webPanelEnabled = configuration.GetValue<bool>("OpenJibo:WebPanel:Enabled"),
                refreshIntervalSeconds = configuration.GetValue<int>("OpenJibo:WebPanel:RefreshIntervalSeconds"),
                allowRemoteAccess = configuration.GetValue<bool>("OpenJibo:WebPanel:AllowRemoteAccess")
            }
        });
    }

    [HttpGet("sessions")]
    public ActionResult GetSessions()
    {
        // Since ICloudStateStore doesnt have a GetAllSessions method for now ill just return a empty list - TO BE UPGRADED!!
        return Ok(new
        {
            sessions = Array.Empty<object>(),
            count = 0
        });
    }

    [HttpGet("robots")]
    public ActionResult GetRobots()
    {
        var robot = stateStore.GetRobot();
        var robotProfile = stateStore.GetRobotProfile();

        return Ok(new
        {
            robots = new[]
            {
                new
                {
                    deviceId = robot.DeviceId,
                    robotId = robot.RobotId,
                    friendlyName = robot.FriendlyName,
                    firmwareVersion = robot.FirmwareVersion,
                    applicationVersion = robot.ApplicationVersion,
                    profile = new
                    {
                        robotId = robotProfile.RobotId,
                        connectedAt = robotProfile.UpdatedUtc.ToString("o"),
                        platform = robotProfile.Payload?.TryGetValue("platform", out var platformValue) == true ? platformValue?.ToString() : null,
                        serialNumber = robotProfile.Payload?.TryGetValue("serialNumber", out var serialValue) == true ? serialValue?.ToString() : null
                    }
                }
            },
            count = 1
        });
    }

    [HttpGet("health")]
    public ActionResult GetHealth()
    {
        var persistenceInfo = stateStore.GetPersistenceStateInfo();

        return Ok(new
        {
            status = "healthy",
            timestamp = DateTimeOffset.UtcNow.ToString("o"),
            checks = new
            {
                persistence = new
                {
                    status = persistenceInfo.LastSavedUtc.HasValue ? "ok" : "warning",
                    lastSaved = persistenceInfo.LastSavedUtc?.ToString("o"),
                    revision = persistenceInfo.Revision
                },
                stateStore = new
                {
                    status = "ok",
                    type = "InMemoryCloudStateStore"
                }
            }
        });
    }

    [HttpPost("state/save")]
    public ActionResult SaveState()
    {
        try
        {
            stateStore.SavePersistedState();
            return Ok(new { success = true, message = "State saved successfully" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpPost("state/reload")]
    public ActionResult ReloadState()
    {
        try
        {
            stateStore.LoadPersistedState();
            return Ok(new { success = true, message = "State reloaded successfully" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("info")]
    public ActionResult GetInfo()
    {
        var robot = stateStore.GetRobot();
        var persistenceInfo = stateStore.GetPersistenceStateInfo();

        return Ok(new
        {
            serverId = Environment.MachineName,
            serverName = robot.FriendlyName ?? "OpenJibo Server",
            endpoint = Request.Host.Value,
            version = OpenJiboCloudBuildInfo.Version,
            startTime = _startTime.ToString("o"),
            uptime = (DateTimeOffset.UtcNow - _startTime).TotalSeconds,
            robotId = robot.RobotId,
            deviceId = robot.DeviceId,
            stateRevision = persistenceInfo.Revision,
            lastStateSave = persistenceInfo.LastSavedUtc?.ToString("o")
        });
    }

    [HttpGet("metrics")]
    public ActionResult GetMetrics()
    {
        var persistenceInfo = stateStore.GetPersistenceStateInfo();
        var robot = stateStore.GetRobot();
        var loops = stateStore.GetLoops();
        var people = stateStore.GetPeople();
        var media = stateStore.ListMedia();
        var updates = stateStore.ListUpdates();
        var backups = stateStore.GetBackups();

        return Ok(new
        {
            timestamp = DateTimeOffset.UtcNow.ToString("o"),
            server = new
            {
                version = OpenJiboCloudBuildInfo.Version,
                uptime = (DateTimeOffset.UtcNow - _startTime).TotalSeconds,
                startTime = _startTime.ToString("o")
            },
            state = new
            {
                revision = persistenceInfo.Revision,
                lastLoaded = persistenceInfo.LastLoadedUtc?.ToString("o"),
                lastSaved = persistenceInfo.LastSavedUtc?.ToString("o"),
                schemaVersion = persistenceInfo.SchemaVersion
            },
            robot = new
            {
                robotId = robot.RobotId,
                deviceId = robot.DeviceId,
                firmwareVersion = robot.FirmwareVersion,
                applicationVersion = robot.ApplicationVersion
            },
            counts = new
            {
                loops = loops.Count,
                people = people.Count,
                media = media.Count,
                updates = updates.Count,
                backups = backups.Count
            }
        });
    }

    private static List<object> _serverLogs = new();
    private static readonly object _logsLock = new();

    [HttpGet("logs")]
    public ActionResult GetLogs(long since = 0)
    {
        lock (_logsLock)
        {
            // Add some test logs if empty
            if (_serverLogs.Count == 0)
            {
                _serverLogs.Add(new { timestamp = DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeMilliseconds(), level = "info", message = "Server running normally" });
                _serverLogs.Add(new { timestamp = DateTimeOffset.UtcNow.AddSeconds(-5).ToUnixTimeMilliseconds(), level = "info", message = "Health check passed" });
                _serverLogs.Add(new { timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), level = "info", message = "Web panel accessed" });
            }

            // Filter logs 
            var filteredLogs = _serverLogs
                .Where(log => (long)((dynamic)log).timestamp > since)
                .ToList();

            return Ok(new
            {
                logs = filteredLogs,
                count = filteredLogs.Count
            });
        }
    }

    [HttpGet("endpoints")]
    public ActionResult GetEndpoints()
    {
        var multiPortEnabled = configuration.GetValue<bool>("OpenJibo:MultiPortMode:Enabled");
        
        if (multiPortEnabled)
        {
            return Ok(new
            {
                mode = "multi-port",
                enabled = true,
                ports = new
                {
                    api = configuration.GetValue<int>("OpenJibo:MultiPortMode:Ports:Api"),
                    apiSocket = configuration.GetValue<int>("OpenJibo:MultiPortMode:Ports:ApiSocket"),
                    neoHubListen = configuration.GetValue<int>("OpenJibo:MultiPortMode:Ports:NeoHubListen"),
                    neoHubProactive = configuration.GetValue<int>("OpenJibo:MultiPortMode:Ports:NeoHubProactive"),
                    webPanel = configuration.GetValue<int>("OpenJibo:MultiPortMode:Ports:WebPanel")
                },
                robotConfig = new
                {
                    webCoreServerPort = configuration.GetValue<int>("OpenJibo:MultiPortMode:Ports:Api"),
                    jetstreamServiceServerPort = configuration.GetValue<int>("OpenJibo:MultiPortMode:Ports:Api"),
                    jetstreamServiceRegistryPort = configuration.GetValue<int>("OpenJibo:MultiPortMode:Ports:ApiSocket"),
                    hubClientHubPort = configuration.GetValue<int>("OpenJibo:MultiPortMode:Ports:NeoHubListen"),
                    hubClientProactivePort = configuration.GetValue<int>("OpenJibo:MultiPortMode:Ports:NeoHubProactive")
                }
            });
        }
        else
        {
            return Ok(new
            {
                mode = "dns-based",
                enabled = false,
                description = "Server uses DNS-based routing. Configure robot hostnames to point to this server.",
                hosts = new
                {
                    api = "api.jibo.com",
                    apiSocket = "api-socket.jibo.com",
                    neoHub = "neo-hub.jibo.com"
                }
            });
        }
    }

    [HttpPost("endpoints/multi-port/enable")]
    public ActionResult EnableMultiPortMode([FromBody] MultiPortConfigRequest request)
    {
        try
        {
            // This is a placeholder for future web panel integration
            // For now, users need to manually edit appsettings.json
            return Ok(new { success = false, message = "Please manually edit appsettings.json to enable multi-port mode. Set OpenJibo:MultiPortMode:Enabled to true and configure the ports." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }
}

public class MultiPortConfigRequest
{
    public int? Api { get; set; }
    public int? ApiSocket { get; set; }
    public int? NeoHubListen { get; set; }
    public int? NeoHubProactive { get; set; }
    public int? WebPanel { get; set; }
}
