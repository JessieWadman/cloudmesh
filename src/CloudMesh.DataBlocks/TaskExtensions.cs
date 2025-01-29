namespace CloudMesh.DataBlocks;

public static class TaskExtensions
{
    /// <summary>
    /// Lets the task run in the background, and submits the result of the tasks to the given DataBlock for processing. 
    /// </summary>
    /// <param name="task">Task to defer execution of</param>
    /// <param name="target">Target DataBlock to submit the result to</param>
    /// <param name="sender">Sender for the message</param>
    /// <typeparam name="T">The type returned by the task</typeparam>
    /// <remarks>
    /// Important note! This method submits the Exception thrown by the task, if the task fails, instead of the result!
    /// Pro tip: The task you pass into this method should NEVER throw unhandled exceptions.
    /// </remarks>
    public static void PipeTo<T>(this Task<T> task, ICanSubmit target, IDataBlockRef? sender)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(target);
            
        // Note: If target has been disposed, this will result in an ObjectDisposedException.
        // We have the option of crashing and burning, or silently ignoring the error, and letting the message be
        // lost.

        YieldAndWait().ContinueWith(result =>
        {
            if (result.IsFaulted)
            {
                Exception error = result.Exception!;
                if (error is AggregateException ae)
                {
                    error = ae.InnerExceptions.Count == 1 
                        ? ae.InnerExceptions[0] 
                        : ae.Flatten();
                }

                _ = target.SubmitAsync(error, sender);
            }
            else if (result.Result != null)
                _ = target.SubmitAsync(result.Result, sender);
        }, TaskContinuationOptions.NotOnCanceled);
        
        return;

        // The Task.Yield here guarantees that the synchronous code above immediately returns and doesn't block, even
        // if the code behind the task passed to us runs synchronously.
        async Task<T> YieldAndWait()
        {
            await Task.Yield();
            return await task;
        }
    }
}