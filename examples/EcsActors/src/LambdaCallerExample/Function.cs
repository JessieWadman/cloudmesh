using Amazon.Lambda.Core;
using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;
using CloudMesh.Actors;
using CloudMesh.Routing;
using EcsActorsExample.Contracts;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Text.Json;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

var cloudMapNamespace = Environment.GetEnvironmentVariable("cloudMapNamespace");

var serviceConfiguration = new ServiceCollection();
using var services = serviceConfiguration.BuildServiceProvider();

// Scheduled lambda, so we ignore incoming request. It's just a timer event.
var handler = async (PlaceOrderRequest request, ILambdaContext context) =>
{
    context.Logger.LogLine(JsonSerializer.Serialize(request));
    return new { ret = new { OrderNo = 1, CustomerName = request.CustomerName, Comment = request.Comment } };
    /*
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
    }*/
};

await LambdaBootstrapBuilder.Create(handler, new DefaultLambdaJsonSerializer())
    .Build()
    .RunAsync();

class PlaceOrderRequest
{
    public string CustomerName { get; set; }
    public string Comment { get; set; }
}

