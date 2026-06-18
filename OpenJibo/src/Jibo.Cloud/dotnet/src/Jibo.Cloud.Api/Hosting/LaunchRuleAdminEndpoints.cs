using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;

namespace Jibo.Cloud.Api.Hosting;

internal static class LaunchRuleAdminEndpoints
{
    internal static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/admin/launch-rules");

        group.MapGet("/", ListLaunchRules);
        group.MapGet("/{fileName}", GetLaunchRule);
        group.MapPost("/", UploadLaunchRules);
        group.MapDelete("/{fileName}", DeleteLaunchRule);
    }

    private static IResult ListLaunchRules(IRobotLaunchRuleStore store)
    {
        var rules = store.List();
        return Results.Ok(new
        {
            scope = "global",
            rules = rules.Select(rule => new
            {
                fileName = rule.FileName,
                sizeBytes = rule.SizeBytes,
                uploadedUtc = rule.UploadedUtc
            })
        });
    }

    private static IResult GetLaunchRule(string fileName, IRobotLaunchRuleStore store)
    {
        if (!LaunchRuleFileValidator.TryNormalizeFileName(fileName, out var normalizedFileName, out var fileError))
            return Results.BadRequest(new { error = fileError });

        var rule = store.Get(normalizedFileName);
        return rule is null
            ? Results.NotFound(new { error = "Launch rule file was not found." })
            : Results.Ok(new
            {
                scope = "global",
                fileName = rule.FileName,
                sizeBytes = rule.SizeBytes,
                uploadedUtc = rule.UploadedUtc,
                content = rule.Content
            });
    }

    private static async Task<IResult> UploadLaunchRules(
        HttpRequest request,
        IRobotLaunchRuleStore store,
        CancellationToken cancellationToken)
    {
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
                var record = store.Save(normalizedFileName, content);
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
            scope = "global",
            uploaded = saved
        });
    }

    private static IResult DeleteLaunchRule(string fileName, IRobotLaunchRuleStore store)
    {
        if (!LaunchRuleFileValidator.TryNormalizeFileName(fileName, out var normalizedFileName, out var fileError))
            return Results.BadRequest(new { error = fileError });

        return store.Delete(normalizedFileName)
            ? Results.Ok(new { scope = "global", deleted = normalizedFileName })
            : Results.NotFound(new { error = "Launch rule file was not found." });
    }
}
