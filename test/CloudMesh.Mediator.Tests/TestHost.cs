using CloudMesh.Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace CloudMesh.Mediator.Tests;

/// <summary>Builds a configured provider scanning this test assembly for handlers and behaviors.</summary>
internal static class TestHost
{
    public static ServiceProvider Build(Action<MediatorOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<Recorder>();
        services.AddCloudMeshMediator(options =>
        {
            options.RegisterServicesFromAssemblyContaining<Ping>();
            configure?.Invoke(options);
        });
        return services.BuildServiceProvider();
    }

    public static IMediator Mediator(this ServiceProvider provider) => provider.GetRequiredService<IMediator>();
    public static Recorder Recorder(this IServiceProvider provider) => provider.GetRequiredService<Recorder>();
}
