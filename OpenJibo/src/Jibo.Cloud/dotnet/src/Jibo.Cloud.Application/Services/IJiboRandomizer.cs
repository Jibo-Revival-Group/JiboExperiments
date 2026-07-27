namespace Jibo.Cloud.Application.Services;

public interface IJiboRandomizer
{
    T Choose<T>(IReadOnlyList<T> items);

    double NextUnitInterval() => Random.Shared.NextDouble();
}

public sealed class DefaultJiboRandomizer : IJiboRandomizer
{
    public T Choose<T>(IReadOnlyList<T> items)
    {
        return items.Count == 0
            ? throw new InvalidOperationException("Cannot choose from an empty list.")
            : items[Random.Shared.Next(items.Count)];
    }
}