namespace CloudMesh.DataBlocks.Tests
{
    public class IdleTimeoutTests
    {
        
        public class TimeoutTestBlock : DataBlock
        {
            public TaskCompletionSource Completed = new();

            public TimeoutTestBlock()
            {
                ReceiveAsync<string>(async _ =>
                {
                    await Task.Delay(100);
                });
            }

            protected override ValueTask BeforeStart()
            {
                SetIdleTimeout(TimeSpan.FromMilliseconds(300));
                return ValueTask.CompletedTask;
            }

            protected override ValueTask AfterStop()
            {
                Completed.SetResult();
                return ValueTask.CompletedTask;
            }
        }

        [Fact]
        public async void IdleTimeoutShouldWork()
        {
            await using var block = new TimeoutTestBlock();
            await block.SubmitAsync("1", null);
            await Task.Delay(200);
            Assert.False(block.Completed.Task.IsCanceled);
            Assert.False(block.Completed.Task.IsCompleted);
            await block.SubmitAsync("2", null);
            await Task.Delay(200);
            Assert.False(block.Completed.Task.IsCanceled);
            Assert.False(block.Completed.Task.IsCompleted);
            await block.SubmitAsync("3", null);
            await Task.Delay(200);
            Assert.False(block.Completed.Task.IsCanceled);
            Assert.False(block.Completed.Task.IsCompleted);

            var timeoutDelay = block.Completed.Task;
            var longerDelayThanTimeout = Task.Delay(500);
            
            var actualTaskCompletedFirst = await Task.WhenAny(timeoutDelay, longerDelayThanTimeout);
            Assert.Equal(timeoutDelay, actualTaskCompletedFirst);
        }
    }
}