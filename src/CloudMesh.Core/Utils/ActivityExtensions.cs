namespace System.Diagnostics
{
    public static class ActivityExtensions
    {
        public static void Complete(this Activity? activity)
            => activity?.SetTag("otel.status_code", "OK");

        public static void Fail(this Activity? activity, Exception error)
            => Fail(activity, $"{error.GetType().FullName}: {error.Message}", error.StackTrace);

        public static void Fail(this Activity? activity, string error, string? stackTrace = null)
        {
            activity?.SetTag("otel.status_code", "ERROR");
            activity?.SetTag("otel.status_description", error);
            activity?.SetTag("exception.stack_trace", stackTrace);
        }
    }
}
