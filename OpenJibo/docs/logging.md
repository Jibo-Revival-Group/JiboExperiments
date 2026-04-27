# Logging argument!
- - -

using the new `DetailedOperationLogger` class you can do tiered logging , from level 1 -10

you can `LogStep` at any level, and it will only log if the log level is 4+
`logstate` at any level, and it will only log if the log level is 5+ (state tracking)
`logDecision` at any level, and it will only log if the log level is 3+ (decision points)
`logTiming` at any level, and it will only log if the log level is 6= (timing performance metrics)
`logPayload` at any level, and it will only log if the log level is 8+ (payload data)
`logExternalCall` at any level, and it will only log if the log level is 5+ (external service calls)
`LogMatch` at any level, and it will only log if the log level is 4+ (pattern matching)


i didnt touch the existing logging but its easy to implement the new logging system in the existing code

you can see implementations at:
- OpenJibo/src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Infrastructure/Telemetry/FileWebSocketTelemetrySink.cs
- OpenJibo/src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Infrastructure/Telemetry/FileWebSocketTelemetrySink.cs

the parser is also inside :
`OpenJibo/src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Api/Logging/LogLevelConfigurator.cs`