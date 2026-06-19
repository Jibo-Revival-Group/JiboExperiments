namespace Jibo.Cloud.Application.Abstractions;

public sealed record KnowledgeSearchRequest(
    string Query,
    SearchBackendKind Backend,
    bool UseFallbackSettings);
