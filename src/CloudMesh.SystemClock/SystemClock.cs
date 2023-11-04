namespace CloudMesh.Utils
{
    /// <summary>
    /// Inversion of control for system clock, so that unit tests can use predictable, or well-known points in time.
    /// In dotnet 8, this functionality is instead provided by the built-in TimeProvider class.
    /// </summary>
    /// <remarks>
    /// The default implementation just pipes it onwards to DateTimeOffset.Now.
    /// </remarks>    
#if (NET8_0_OR_GREATER)
    [Obsolete("Use .NET built-in TimeProvider instead")]
#endif
    public interface ISystemClock
    {
        DateTimeOffset NowWithOffset { get; }
        DateTime UtcNow { get; }

        DateTimeOffset UtcNowWithOffset => NowWithOffset.ToUniversalTime();
        DateOnly UtcToday => DateOnly.FromDateTime(UtcNow);
    }

#if (NET8_0_OR_GREATER)
    [Obsolete("Use .NET built-in TimeProvider instead")]
#endif
    public class SystemClock : ISystemClock
    {
        public DateTimeOffset NowWithOffset => DateTimeOffset.Now;
        public DateTime UtcNow => DateTime.UtcNow;
    }

#if (NET8_0_OR_GREATER)
    [Obsolete("Use .NET built-in TimeProvider instead")]
#endif
    public class MockSystemClock : ISystemClock
    {
        public DateTimeOffset NowWithOffset => UtcNow;
        public DateTime UtcNow { get; set; } = DateTime.UtcNow;
        public DateOnly UtcToday => DateOnly.FromDateTime(UtcNow);
    }
}

namespace Microsoft.Extensions.DependencyInjection
{
    using CloudMesh.Utils;

    public static class SystemClockServiceCollectionExtensions
    {
#if (NET8_0_OR_GREATER)
    [Obsolete("Use .NET built-in TimeProvider instead")]
#endif
        public static IServiceCollection AddSystemClock(this IServiceCollection services)
        {
            services.AddSingleton<ISystemClock, SystemClock>();
            return services;
        }

#if (NET8_0_OR_GREATER)
    [Obsolete("Use .NET built-in TimeProvider instead")]
#endif
        public static ISystemClock GetSystemClock(this IServiceProvider services)
            => services.GetRequiredService<ISystemClock>() ?? throw new InvalidOperationException("System clock is not registered");
    }
}