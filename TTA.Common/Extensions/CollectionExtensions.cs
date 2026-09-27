namespace TTA.Common.Extensions;

/// <summary>
/// Provides extension methods for collections and enumerables.
/// </summary>
public static class CollectionExtensions
{
    /// <summary>
    /// Determines whether the enumerable is null or contains no elements.
    /// </summary>
    /// <typeparam name="T">The type of elements in the collection.</typeparam>
    /// <param name="enumerable">The enumerable to check.</param>
    /// <returns>True if the enumerable is null or empty; otherwise, false.</returns>
    public static bool IsNullOrEmpty<T>(this IEnumerable<T>? enumerable)
    {
        return enumerable == null || !enumerable.Any();
    }

    /// <summary>
    /// Maps an enumerable of one type to a list of another type using the provided mapping function.
    /// </summary>
    /// <typeparam name="TIn">The type of the input elements.</typeparam>
    /// <typeparam name="TOut">The type of the output elements.</typeparam>
    /// <param name="list">The source collection to map.</param>
    /// <param name="map">The function to transform each element.</param>
    /// <returns>A list containing the transformed elements.</returns>
    public static List<TOut> MapToList<TIn, TOut>(this IEnumerable<TIn> list, Func<TIn, TOut> map)
        => [.. list.Select(map)];
}