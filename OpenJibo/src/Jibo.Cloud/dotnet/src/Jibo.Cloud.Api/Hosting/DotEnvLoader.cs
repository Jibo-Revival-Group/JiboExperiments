namespace Jibo.Cloud.Api.Hosting;

internal static class DotEnvLoader
{
    public static void LoadIntoEnvironment(string? envFilePath)
    {
        if (string.IsNullOrWhiteSpace(envFilePath) || !File.Exists(envFilePath))
            return;

        foreach (var rawLine in File.ReadAllLines(envFilePath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0) continue;

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim().Trim('"');
            if (key.Length == 0) continue;

            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
