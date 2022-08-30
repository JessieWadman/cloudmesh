namespace System
{
    public static class AsyncExtensions
    {
        public static async Task<T[]> ToArrayAsync<T>(this IAsyncEnumerable<T> enumerable)
        {
            List<T> items = new();
            await foreach (var item in enumerable)
            {
                items.Add(item);
            }
            return items.ToArray();
        }

        public static async IAsyncEnumerable<T> Take<T>(this IAsyncEnumerable<T> source, int count)
        {
            var itemsReturned = 0;
            await foreach (var item in source)
            {
                yield return item;
                if (++itemsReturned >= count)
                    yield break;
            }
        }
    }
}
