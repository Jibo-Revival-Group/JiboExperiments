namespace Jibo.Cloud.Application.Abstractions;

public interface IKnowledgeSearchService
{
    bool IsConfigured { get; }

    Task<KnowledgeSearchResult?> SearchAsync(string query, CancellationToken cancellationToken = default);
}