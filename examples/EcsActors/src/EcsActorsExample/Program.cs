using CloudMesh.Routing;
using EcsActorsExample.Actors;
using EcsActorsExample.Contracts;
using EcsActorsExample.Services;
using System.Collections.Immutable;

var builder = WebApplication.CreateBuilder(args);

// builder.Services.AddHostedService<SingletonTest>();
builder.Services.AddActor<ICart, Cart>();
builder.Services.AddService<ICartService, CartService>();

builder.Services.AddHostedService<RandomInvokeService>();

var app = builder.Build();

#if (DEBUG)

var mockResolver = new MockRouteResolver();
Router.RouteResolver = mockResolver;

app.Services.GetRequiredService<IHostApplicationLifetime>()
    .ApplicationStarted.Register(() =>
    {
        var localIp = app.Urls
            .Select(u => new Uri(u))
            .Where(u => u.Scheme == "http")
            .Select(u => $"{u.Host}:{u.Port}")
            .First();
        mockResolver.Set<ICart>("Actors", new[] { new ResourceInstance("1", ResourceIdentifier.Parse($"http://{localIp}"), ImmutableDictionary<string, string>.Empty) });
        mockResolver.Set<ICartService>("Services", new[] { new ResourceInstance("1", ResourceIdentifier.Parse($"http://{localIp}"), ImmutableDictionary<string, string>.Empty) });
        /*
        
        mockResolver.Set("Storage", "StateStore", new[] { new ResourceInstance("1", new("sql", "localhost:1433"), ImmutableDictionary<string, string>.Empty.Add("username", "sa")) });
        mockResolver.Set<IOrderService>("Services", new[] { new ResourceInstance("1", ResourceIdentifier.Parse("lambda://invocation-test"), ImmutableDictionary<string, string>.Empty) });*/
    });
#endif 

if (app.Environment.IsDevelopment())
    app.UseDeveloperExceptionPage();
else
    app.UseHttpsRedirection();

app.UseRouting();
app.MapActors();
app.MapServices();

#if (RELEASE)
app.Urls.Add("http://+:5000");
#endif

await app.RunAsync();