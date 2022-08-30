namespace CloudMesh.Mock
{
    // This is used in place of DateTimeOffset.Now and so on throughout the solution, so that
    // we can control the current date and time in unit tests.
    // This is important if we have static files that we simulate import of where timestamps
    // are old. It allows us to rewind the clock to the point in time when the file
    // was current.
    // It also allows us to fast-forward time from effective dates to termination dates
    // and so on, as part of the unit tests.

    // The default implementation just pipes it onwards to DateTimeOffset.Now    
    public interface ISystemClock
    {
        DateTimeOffset NowWithOffset { get; }
        DateTime UtcNow { get; }

        DateTimeOffset UtcNowWithOffset => NowWithOffset.ToUniversalTime();
        DateOnly UtcToday => DateOnly.FromDateTime(UtcNow);
    }

    public class SystemClock : ISystemClock
    {
        public DateTimeOffset NowWithOffset => DateTimeOffset.Now;
        public DateTime UtcNow => DateTime.UtcNow;
    }

    public class MockSystemClock : ISystemClock
    {
        public DateTimeOffset NowWithOffset => UtcNow;
        public DateTime UtcNow { get; set; } = DateTime.UtcNow;
        public DateOnly UtcToday => DateOnly.FromDateTime(UtcNow);
    }
}

namespace Microsoft.Extensions.DependencyInjection
{
    using CloudMesh.Mock;

    public static class SystemClockServiceCollectionExtensions
    {
        public static IServiceCollection AddSystemClock(this IServiceCollection services)
        {
            services.AddSingleton<ISystemClock, SystemClock>();
            return services;
        }

        public static ISystemClock GetSystemClock(this IServiceProvider services)
            => services.GetRequiredService<ISystemClock>() ?? throw new InvalidOperationException("System clock is not registered");
    }
}
