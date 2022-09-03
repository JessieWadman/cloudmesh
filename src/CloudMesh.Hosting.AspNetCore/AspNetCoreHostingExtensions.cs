using CloudMesh.Actors;
using CloudMesh.Actors.Hosting;
using CloudMesh.Hosting.AspNetCore;
using CloudMesh.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Net;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class AspNetCoreHostingExtensions
    {
        private static bool actorInfrastructureAdded = false;
        private static void AddActorInfrastructure(IServiceCollection services)
        {
            if (actorInfrastructureAdded)
                return;
            services.AddSingleton<IActorHost, ActorHost>();
        }

        public static IServiceCollection AddActor<TActor, TImplementation>(this IServiceCollection services)
            where TActor : class, IActor
            where TImplementation : Actor, TActor
        {
            AddActorInfrastructure(services);
            ActorTypes.Register<TImplementation>(typeof(TActor).Name);
            return services;
        }

        public static IServiceCollection AddService<TService, TImplementation>(this IServiceCollection services)
            where TService : class, IService
            where TImplementation : class, TService
        {
            services.AddScoped<TService, TImplementation>();
            ServiceTypes.ServiceNamesToTypes[typeof(TService).Name] = typeof(TService);
            return services;
        }

        public static IEndpointConventionBuilder MapActors(this IEndpointRouteBuilder endpoints)
        {
            endpoints.ServiceProvider.GetRequiredService<IActorHost>();

            return endpoints.MapPut("/actors/{type}/{id}/{methodName}", (HttpContext context, string type, string id, string methodName, CancellationToken cancellationToken) 
                => AspNetCoreActorHandler.HandleAsync(context, type, id, methodName, cancellationToken));
        }

        public static IEndpointConventionBuilder MapServices(this IEndpointRouteBuilder endpoints)
        {
            endpoints.ServiceProvider.GetRequiredService<IActorHost>();

            return endpoints.MapPut("/services/{type}/{methodName}", (HttpContext context, string type, string methodName, CancellationToken cancellationToken) =>
                AspNetCoreServiceHandler.HandleAsync(context, type, methodName, cancellationToken));
        }
    }
}