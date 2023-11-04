namespace CloudMesh.DataBlocks
{
    internal static class TaskHelper
    {
        public static readonly ValueTask CompletedTask = new();
        public static readonly ValueTask<bool> True = new(true);
        public static readonly ValueTask<bool> False = new(false);

        public static ValueTask WhenAll(params Task[] tasks)
            => new(Task.WhenAll(tasks));

        public static ValueTask WhenAll(params ValueTask[] tasks)
        {
            var remainder = tasks.Where(t => !t.IsCompleted).ToArray();
            return remainder.Length switch
            {
                0 => CompletedTask,
                1 => new ValueTask(remainder[0].AsTask()),
                _ => new ValueTask(Task.WhenAll(remainder.Select(t => t.AsTask())))
            };
        }
    }
}
