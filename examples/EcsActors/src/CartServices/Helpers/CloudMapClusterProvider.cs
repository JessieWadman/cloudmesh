using Amazon.ServiceDiscovery;
using Proto.Cluster;

namespace CartServices.Helpers
{
    public interface ICloudMapAutoRegisteredInstancesKindBuilder
    {
        ICloudMapAutoRegisteredInstancesConfigBuilder Kinds(params string[] kinds);
    }

    public static class CloudMapAutoRegisteredInstancesKindBuilderExtensions
    {
        public static ICloudMapAutoRegisteredInstancesConfigBuilder Kinds<T1>(this ICloudMapAutoRegisteredInstancesKindBuilder builder)
            => builder.Kinds(typeof(T1).Name);

        public static ICloudMapAutoRegisteredInstancesConfigBuilder Kinds<T1, T2>(this ICloudMapAutoRegisteredInstancesKindBuilder builder)
            => builder.Kinds(typeof(T1).Name, typeof(T2).Name);

        public static ICloudMapAutoRegisteredInstancesConfigBuilder Kinds<T1, T2, T3>(this ICloudMapAutoRegisteredInstancesKindBuilder builder)
            => builder.Kinds(typeof(T1).Name, typeof(T2).Name, typeof(T3).Name);

        public static ICloudMapAutoRegisteredInstancesConfigBuilder Kinds<T1, T2, T3, T4>(this ICloudMapAutoRegisteredInstancesKindBuilder builder)
            => builder.Kinds(typeof(T1).Name, typeof(T2).Name, typeof(T3).Name, typeof(T4).Name);

        public static ICloudMapAutoRegisteredInstancesConfigBuilder Kinds<T1, T2, T3, T4, T5>(this ICloudMapAutoRegisteredInstancesKindBuilder builder)
            => builder.Kinds(typeof(T1).Name, typeof(T2).Name, typeof(T3).Name, typeof(T4).Name, typeof(T5).Name);

        public static ICloudMapAutoRegisteredInstancesConfigBuilder Kinds<T1, T2, T3, T4, T5, T6>(this ICloudMapAutoRegisteredInstancesKindBuilder builder)
            => builder.Kinds(typeof(T1).Name, typeof(T2).Name, typeof(T3).Name, typeof(T4).Name, typeof(T5).Name, typeof(T6).Name);

        public static ICloudMapAutoRegisteredInstancesConfigBuilder Kinds<T1, T2, T3, T4, T5, T6, T7>(this ICloudMapAutoRegisteredInstancesKindBuilder builder)
            => builder.Kinds(typeof(T1).Name, typeof(T2).Name, typeof(T3).Name, typeof(T4).Name, typeof(T5).Name, typeof(T6).Name, typeof(T7).Name);

        public static ICloudMapAutoRegisteredInstancesConfigBuilder Kinds<T1, T2, T3, T4, T5, T6, T7, T8>(this ICloudMapAutoRegisteredInstancesKindBuilder builder)
            => builder.Kinds(typeof(T1).Name, typeof(T2).Name, typeof(T3).Name, typeof(T4).Name, typeof(T5).Name, typeof(T6).Name, typeof(T7).Name, typeof(T8).Name);
    }

    public interface ICloudMapAutoRegisteredInstancesServiceBuilder
    {
        ICloudMapAutoRegisteredInstancesKindBuilder CloudMapServiceName(string cloudMapServiceName, int defaultPort);
    }

    public interface ICloudMapAutoRegisteredInstancesConfigBuilder
    {
        ICloudMapAutoRegisteredInstancesServiceBuilder CloudMapNamespace(string cloudMapNamespaceName);
    }

    internal class CloudMapClusterConfig : 
        ICloudMapAutoRegisteredInstancesConfigBuilder,
        ICloudMapAutoRegisteredInstancesServiceBuilder,
        ICloudMapAutoRegisteredInstancesKindBuilder
    {
        private readonly Action<string, string, int, string[]> emit;
        private string cloudMapNamespaceName = string.Empty;
        private string cloudMapServiceName = string.Empty;
        private int defaultPort = 0;

        public CloudMapClusterConfig(Action<string, string, int, string[]> emit)
        {
            this.emit = emit;
        }

        public ICloudMapAutoRegisteredInstancesServiceBuilder CloudMapNamespace(string cloudMapNamespaceName)
        {
            this.cloudMapNamespaceName = cloudMapNamespaceName;
            return this;
        }

        public ICloudMapAutoRegisteredInstancesKindBuilder CloudMapServiceName(string cloudMapServiceName, int defaultPort)
        {
            this.cloudMapServiceName = cloudMapServiceName;
            this.defaultPort = defaultPort;
            return this;
        }

        public ICloudMapAutoRegisteredInstancesConfigBuilder Kinds(params string[] kinds)
        {
            emit(cloudMapNamespaceName, cloudMapServiceName, defaultPort, kinds);
            return this;
        }
    }

    public static class CloudMapClusterProvider
    {
        /// <summary>
        /// For when ECS automatically registers instances in CloudMap.
        /// This will yield one CloudMap service for each Task running in ECS. If the app is split into multiple tasks, then 
        /// we must query multiple CloudMap services to find all instances.
        /// </summary>
        /// <param name="config">Build to configure CloudMap namespace, services and ports.</param>
        /// <param name="options">Optional options for the provider</param>
        /// <param name="client">Optional pre-configured client</param>
        /// <returns>A cluster provider</returns>
        public static IClusterProvider EcsAutoRegisteredInstances(
            Action<ICloudMapAutoRegisteredInstancesConfigBuilder> config, 
            AmazonCloudMapProviderOptions? options = null,
            AmazonServiceDiscoveryClient? client = null)
        {
            var entries = new List<CloudMapAutoRegisteredServiceEntry>();
            var configBuilder = new CloudMapClusterConfig((cloudMapNamespace, cloudMapServiceName, defaultPort, kinds) =>
            {
                entries.Add(new(cloudMapNamespace, cloudMapServiceName, defaultPort, kinds));
            });
            config(configBuilder);

            return new AmazonAutoRegisteredCloudMapProvider(entries.Distinct().ToArray(), options, client);
        }

        /// <summary>
        /// For when you want to manually register each instance in CloudMap. 
        /// Note: There is no way to update existing instances in CloudMap. If a process crashes, the 
        /// instance will linger and there's currently no way to clean them up.
        /// </summary>
        /// <param name="cloudMapNamespace">CloudMap namespace to use</param>
        /// <param name="cloudMapServiceName">CloudMap service name to use to register instances.</param>
        /// <param name="options">Optional options for the provider</param>
        /// <param name="client">Optional pre-configured client to use</param>
        /// <returns>A cluster provider</returns>
        public static IClusterProvider ManuallyRegisteredInstances(
            string cloudMapNamespace, 
            string cloudMapServiceName, 
            AmazonCloudMapProviderOptions? options = null,
            AmazonServiceDiscoveryClient? client = null)
        {
            return new AmazonManuallyRegisteredCloudMapProvider(cloudMapNamespace, cloudMapServiceName, options, client);
        }
    }
}
