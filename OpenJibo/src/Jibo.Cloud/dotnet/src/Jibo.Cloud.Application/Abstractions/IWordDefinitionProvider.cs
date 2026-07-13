namespace Jibo.Cloud.Application.Abstractions;

public interface IWordDefinitionProvider
{
    Task<string?> GetDefinitionAsync(string word, CancellationToken cancellationToken = default);
}
