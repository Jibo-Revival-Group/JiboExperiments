namespace Jibo.Cloud.Application.Abstractions;

public interface IKnowledgeSearchProvider
{
    SearchBackendKind Kind { get; }

    Task<KnowledgeSearchResult?> SearchAsync(
        KnowledgeSearchRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record KnowledgeSearchResult(
    string AnswerText,
    SearchBackendKind BackendKind);
