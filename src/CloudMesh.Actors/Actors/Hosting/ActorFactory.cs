using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;

namespace CloudMesh.Actors.Hosting
{
    internal class ActorFactory
    {
        private static readonly PropertyInfo idProp = typeof(Actor).GetProperty("Id", BindingFlags.Instance | BindingFlags.Public)!;
        private static readonly PropertyInfo actorNameProp = typeof(Actor).GetProperty("ActorName", BindingFlags.Instance | BindingFlags.Public)!;
        private static readonly PropertyInfo servicesProp = typeof(Actor).GetProperty("Services", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly PropertyInfo configurationProp = typeof(Actor).GetProperty("Configuration", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly PropertyInfo loggerProp = typeof(Actor).GetProperty("Logger", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly ConstructorInfo defaultConstructor = typeof(Actor).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes)!;

        private static readonly ConcurrentDictionary<Type, ConstructorInfo> constructors = new();

        private static readonly Dictionary<Type, Func<string, string, IHostedActor>> factories = new();

        private static readonly Counter<int> createCounter = Metrics.Meter.CreateCounter<int>("actorsCreated");

        public static IHostedActor Create(
            string actorName,
            Type actorType,
            string id,
            IServiceProvider services,
            IConfiguration configuration,
            ILoggerFactory loggerFactory)
        {
            var logger = loggerFactory.CreateLogger($"{actorName}#{id}");

            var actor = FormatterServices.GetSafeUninitializedObject(actorType);
            idProp.SetValue(actor, id);
            actorNameProp.SetValue(actor, actorName);
            servicesProp.SetValue(actor, services);
            configurationProp.SetValue(actor, configuration);
            loggerProp.SetValue(actor, logger);

            var constructor = constructors.GetOrAdd(actorType, GetConstructor);

            constructor.Invoke(actor, Array.Empty<object>());

            createCounter.Add(1);

            return (IHostedActor)actor!;
        }

        private static ConstructorInfo GetConstructor(Type type)
        {
            return ((NewExpression)Expression.Lambda(Expression.New(type)).Body)!.Constructor!;
        }
    }
}
