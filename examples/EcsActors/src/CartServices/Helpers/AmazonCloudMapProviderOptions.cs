namespace CartServices.Helpers
{
    public record AmazonCloudMapProviderOptions(int PollIntervalSeconds = 5, bool DeveloperLogging = false)
    {
        internal LogLevel DebugLogLevel => DeveloperLogging ? LogLevel.Information : LogLevel.Debug;
    }
}
