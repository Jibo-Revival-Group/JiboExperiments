namespace Jibo.Cloud.Application.Abstractions;

public sealed record KnowledgeSearchRequest(
    string Query,
    SearchBackendSpec BackendSpec);
