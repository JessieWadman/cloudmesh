using Amazon.Lambda.Core;
using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;
using CloudMesh.Actors.Client;
using CloudMesh.Actors.Routing;
using EcsActorsExample.Contracts;
using Microsoft.Extensions.DependencyInjection;
using System;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

var cloudMapNamespace = Environment.GetEnvironmentVariable("cloudMapNamespace");

var serviceConfiguration = new ServiceCollection();
serviceConfiguration.AddActorClient(opts =>
{
    ActorClientAwsExtensions.AddCloudMapServiceDiscovery(opts, cloudMapNamespace);
    opts.AddProxy<ICart>(serviceName: "CartService", actorName: ActorNames.Cart);
});

using var services = serviceConfiguration.BuildServiceProvider();

// Scheduled lambda, so we ignore incoming request. It's just a timer event.
var handler = async (IgnoreRequest _, ILambdaContext context) =>
{
    try
    {
        var cartId = Guid.NewGuid().ToString();
        var cart = ActorProxy.Create<ICart>(cartId);

        var order = await cart.PlaceOrderAsync(new CartItem[] { new("shampoo", 4) });
        context.Logger.LogLine($"Order placed for cart {cartId} for a total of {order.GetTotal()}");
    }
    catch (RoutingException r)
    {
        context.Logger.LogLine($"Failed to place order for cart. No instance available of service 'OrderService' available");
    }
};

await LambdaBootstrapBuilder.Create(handler, new DefaultLambdaJsonSerializer())
    .Build()
    .RunAsync();

readonly struct IgnoreRequest { }

