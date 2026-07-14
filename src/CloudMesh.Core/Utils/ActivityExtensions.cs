namespace System.Diagnostics
{
    /// <summary>
    /// Extensions for setting OpenTelemetry status tags on an <see cref="Activity"/>. All methods are null-safe, so
    /// they are no-ops when tracing is disabled and the activity is <see langword="null"/>.
    /// </summary>
    public static class ActivityExtensions
    {
        /// <summary>Marks the activity as successful by setting <c>otel.status_code</c> to <c>OK</c>.</summary>
        /// <param name="activity">The activity, or <see langword="null"/> to do nothing.</param>
        public static void Complete(this Activity? activity)
            => activity?.SetTag("otel.status_code", "OK");

        /// <summary>Marks the activity as failed, recording the exception's type, message, and stack trace.</summary>
        /// <param name="activity">The activity, or <see langword="null"/> to do nothing.</param>
        /// <param name="error">The exception that caused the failure.</param>
        public static void Fail(this Activity? activity, Exception error)
            => Fail(activity, $"{error.GetType().FullName}: {error.Message}", error.StackTrace);

        /// <summary>Marks the activity as failed with an explicit description and optional stack trace.</summary>
        /// <param name="activity">The activity, or <see langword="null"/> to do nothing.</param>
        /// <param name="error">A human-readable failure description recorded as <c>otel.status_description</c>.</param>
        /// <param name="stackTrace">An optional stack trace recorded as <c>exception.stack_trace</c>.</param>
        public static void Fail(this Activity? activity, string error, string? stackTrace = null)
        {
            activity?.SetTag("otel.status_code", "ERROR");
            activity?.SetTag("otel.status_description", error);
            activity?.SetTag("exception.stack_trace", stackTrace);
        }
    }
}
