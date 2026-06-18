using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
namespace Jibo.Cloud.Api.Hosting;

internal static class RobotLaunchRuleEndpoints
{
    internal static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/public/robots/{robotName}/launch-rules");

        group.MapGet("/", ListLaunchRules);
        group.MapGet("/{fileName}", GetLaunchRule);
        group.MapPost("/", UploadLaunchRules);
        group.MapDelete("/{fileName}", DeleteLaunchRule);
    }

    private static IResult ListLaunchRules(string robotName, IRobotLaunchRuleStore store)
    {
        if (!TryValidateRobotName(robotName, out var normalized, out var error))
            return Results.BadRequest(new { error });

        var rules = store.List(normalized);
        return Results.Ok(new
        {
            robotFriendlyName = normalized,
            rules = rules.Select(rule => new
            {
                fileName = rule.FileName,
                sizeBytes = rule.SizeBytes,
                uploadedUtc = rule.UploadedUtc
            })
        });
    }

    private static IResult GetLaunchRule(string robotName, string fileName, IRobotLaunchRuleStore store)
    {
        if (!TryValidateRobotName(robotName, out var normalized, out var robotError))
            return Results.BadRequest(new { error = robotError });

        if (!LaunchRuleFileValidator.TryNormalizeFileName(fileName, out var normalizedFileName, out var fileError))
            return Results.BadRequest(new { error = fileError });

        var rule = store.Get(normalized, normalizedFileName);
        return rule is null
            ? Results.NotFound(new { error = "Launch rule file was not found." })
            : Results.Ok(new
            {
                robotFriendlyName = normalized,
                fileName = rule.FileName,
                sizeBytes = rule.SizeBytes,
                uploadedUtc = rule.UploadedUtc,
                content = rule.Content
            });
    }

    private static async Task<IResult> UploadLaunchRules(
        string robotName,
        HttpRequest request,
        IRobotLaunchRuleStore store,
        CancellationToken cancellationToken)
    {
        if (!TryValidateRobotName(robotName, out var normalized, out var robotError))
            return Results.BadRequest(new { error = robotError });

        if (!request.HasFormContentType)
            return Results.BadRequest(new { error = "Upload launch rule files using multipart form data." });

        var form = await request.ReadFormAsync(cancellationToken);
        var files = form.Files.Where(file => file.Length > 0).ToArray();
        if (files.Length == 0)
            return Results.BadRequest(new { error = "Select at least one .rule file to upload." });

        var saved = new List<object>();
        foreach (var file in files)
        {
            if (!LaunchRuleFileValidator.TryNormalizeFileName(file.FileName, out var normalizedFileName,
                    out var fileError))
                return Results.BadRequest(new { error = fileError });

            await using var stream = file.OpenReadStream();
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync(cancellationToken);

            if (!LaunchRuleFileValidator.TryValidateContent(content, out var contentError))
                return Results.BadRequest(new { error = contentError });

            try
            {
                var record = store.Save(normalized, normalizedFileName, content);
                saved.Add(new
                {
                    fileName = record.FileName,
                    sizeBytes = record.SizeBytes,
                    uploadedUtc = record.UploadedUtc
                });
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        }

        return Results.Ok(new
        {
            robotFriendlyName = normalized,
            uploaded = saved
        });
    }

    private static IResult DeleteLaunchRule(string robotName, string fileName, IRobotLaunchRuleStore store)
    {
        if (!TryValidateRobotName(robotName, out var normalized, out var robotError))
            return Results.BadRequest(new { error = robotError });

        if (!LaunchRuleFileValidator.TryNormalizeFileName(fileName, out var normalizedFileName, out var fileError))
            return Results.BadRequest(new { error = fileError });

        return store.Delete(normalized, normalizedFileName)
            ? Results.Ok(new { robotFriendlyName = normalized, deleted = normalizedFileName })
            : Results.NotFound(new { error = "Launch rule file was not found." });
    }

    private static bool TryValidateRobotName(string robotName, out string normalized, out string error)
    {
        if (RobotFriendlyNameValidator.TryNormalize(robotName, out normalized, out var validationError))
        {
            error = string.Empty;
            return true;
        }

        error = validationError ?? "Robot friendly name is invalid.";
        normalized = string.Empty;
        return false;
    }
}
