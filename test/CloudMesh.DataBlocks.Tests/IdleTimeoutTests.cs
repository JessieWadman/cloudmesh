namespace CloudMesh.DataBlocks.Tests
{
    public class IdleTimeoutTests
    {
        
        public class TimeoutTestBlock : DataBlock
        {
            public TaskCompletionSource Completed = new();

            public TimeoutTestBlock()
            {
                SetIdleTimeout(TimeSpan.FromMilliseconds(300));

                ReceiveAsync<string>(async _ =>
                {
                    await Task.Delay(100);
                });
            }

            protected override ValueTask BeforeStart()
            {
                
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

        public class ParentBlock : DataBlock
        {
            public ParentBlock(Action<IDataBlock> onChildCreated, Action onChildStopped)
            {
                ReceiveAsync<TaskCompletionSource>(tcs =>
                    GetOrAddChild("1", () => new ChildBlock(onChildCreated, onChildStopped)).SubmitAsync(tcs, this)
                );
            }
        }

        public class ChildBlock : DataBlock
        {
            private readonly Action onChildStopped;

            public ChildBlock(Action<IDataBlock> onChildCreated, Action onChildStopped)
            {
                onChildCreated(this);
                this.onChildStopped = onChildStopped;

                SetIdleTimeout(TimeSpan.FromMilliseconds(100));
                ReceiveAsync<TaskCompletionSource>(tcs =>
                {
                    tcs.SetResult();
                    return ValueTask.CompletedTask;
                });
            }

            protected override ValueTask BeforeStart()
            {
                return base.BeforeStart();
            }

            protected override ValueTask AfterStop()
            {
                onChildStopped();
                return ValueTask.CompletedTask;
            }
        }

        [Fact]
        public async Task RestartingChildrenShouldWork()
        {
            var childStopCount = 0;
            var childStartCount = 0;
            ChildBlock? child = null;
            await using var root = new ParentBlock(c =>
            {
                childStartCount++;
                child = (ChildBlock)c;
            },  () => childStopCount++);

            Assert.Equal(0, childStartCount);
            Assert.Equal(0, childStopCount);

            {
                var tcs = new TaskCompletionSource();
                await root.SubmitAsync(tcs, null);
                await tcs.Task;                
            }

            Assert.Equal(1, childStartCount);
            Assert.Equal(0, childStopCount);
            Assert.NotNull(child);

            await Task.Delay(100);

            {
                var tcs = new TaskCompletionSource();
                await root.SubmitAsync(tcs, null);
                await tcs.Task;                
            }

            Assert.Equal(1, childStartCount);
            Assert.Equal(0, childStopCount);

            await Task.Delay(1000);
            Assert.Equal(1, childStartCount);
            Assert.Equal(1, childStopCount);

            {
                var tcs = new TaskCompletionSource();
                await root.SubmitAsync(tcs, null);
                await tcs.Task;
            }

            Assert.Equal(2, childStartCount);
            Assert.Equal(1, childStopCount);

            await Task.Delay(1000);

            Assert.Equal(2, childStartCount);
            Assert.Equal(2, childStopCount);
        }
    }
}