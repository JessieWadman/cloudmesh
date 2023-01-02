namespace CartServices
{
    public static class DictionaryHelpers
    {
        public static V NonThreadSafeGetOrAdd<K, V>(this IDictionary<K, V> dict, K key, Func<V> factory)
        {
            if (dict.TryGetValue(key, out var result))
                return result;
            return dict[key] = factory();
        }
    }
}
