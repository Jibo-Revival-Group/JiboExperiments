namespace Jibo.Cloud.Application.Abstractions;

public interface IFunFactProvider
{
    Task<string?> GetRandomFactAsync(CancellationToken cancellationToken = default);
}
