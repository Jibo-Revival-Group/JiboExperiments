namespace Jibo.Cloud.Application.Abstractions;

public sealed record SearchBackendSpec(
    SearchBackendKind Kind,
    string? Credential,
    string? Model)
{
    public static SearchBackendSpec None { get; } = new(SearchBackendKind.None, null, null);

    public bool IsUsable =>
        Kind switch
        {
            SearchBackendKind.None => false,
            SearchBackendKind.Wolfram or SearchBackendKind.ChatGPT =>
                !string.IsNullOrWhiteSpace(Credential),
            SearchBackendKind.Ollama => true,
            _ => false
        };
}
