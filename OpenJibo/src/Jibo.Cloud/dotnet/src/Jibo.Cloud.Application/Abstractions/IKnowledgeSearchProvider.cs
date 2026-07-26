namespace Jibo.Cloud.Application.Abstractions;

public enum KnowledgeSearchOutcome
{
    Found,
    NotFound,
    Unavailable
}

public interface IKnowledgeSearchProvider
{
    SearchBackendKind Kind { get; }

    Task<KnowledgeSearchResult?> SearchAsync(
        KnowledgeSearchRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record KnowledgeSearchResult(
    string AnswerText,
    SearchBackendKind BackendKind,
    KnowledgeSearchOutcome Outcome = KnowledgeSearchOutcome.Found)
{
    public static KnowledgeSearchResult NotFound(SearchBackendKind backendKind) =>
        new(string.Empty, backendKind, KnowledgeSearchOutcome.NotFound);

    public static KnowledgeSearchResult Unavailable(SearchBackendKind backendKind) =>
        new(string.Empty, backendKind, KnowledgeSearchOutcome.Unavailable);
}
