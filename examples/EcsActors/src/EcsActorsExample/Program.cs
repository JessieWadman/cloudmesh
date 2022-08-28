using CloudMesh.Actors.Routing;
using EcsActorsExample.Actors;
using EcsActorsExample.Contracts;
using EcsActorsExample.Services;
using System.Diagnostics;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// builder.Services.AddHostedService<SingletonTest>();
builder.Services.AddActorHosting(options =>
{
    options.AddActor<Cart>(ActorNames.Cart);
});

builder.Services.AddActorClient(options =>
{
    options.AddProxy<ICart>("CartService", ActorNames.Cart);
    options.AddTestServiceDiscovery();
});

builder.Services.AddHostedService<RandomInvokeService>();

var app = builder.Build();

#if (DEBUG)
var localIp = (string)null;
LocalIpAddressResolver.Instance = LocalIpAddressResolvers.From(() => localIp);

// When debugging locally, port is whatever launchsetting.json says, so it
// can be unpredictable from machine to machine. So we wait for app to start
// and then we determine IP address from Kestrel
app.Services.GetRequiredService<IHostApplicationLifetime>()
    .ApplicationStarted.Register(() =>
    {
        localIp = app.Urls
            .Select(u => new Uri(u))
            .Where(u => u.Scheme == "http")
            .Select(u => $"{u.Host}:{u.Port}")
            .First();
    });
#endif 

if (app.Environment.IsDevelopment())
    app.UseDeveloperExceptionPage();
else
    app.UseHttpsRedirection();

app.UseRouting();
app.MapActors();

#if (RELEASE)
app.Urls.Add("http://+:5000");
#endif

await app.RunAsync();