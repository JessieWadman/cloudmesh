namespace CloudMesh.DataBlocks.Tests;

public class StateMachineTests
{
    [Fact]
    public async Task BehaviorsShouldWork()
    {
        var testProbe = new TestProbe();
        await using var stateMachine = new TestStateMachine(testProbe);
        
        // Expect initial state to be Ready.
        testProbe.Expect<Signals.StateChanged>(500, x => x.State == "Ready");
        
        await stateMachine.SubmitAsync(Signals.Restart.Instance, testProbe);
        testProbe.Expect<Signals.InvalidState>();
        
        await stateMachine.SubmitAsync(Signals.Start.Instance, testProbe);
        // Expect state machine to transition to Running state and to let us know
        testProbe.Expect<Signals.StateChanged>(500, x => x.State == "Running");
        
        // Expect it to reject restart signal while in Running state
        await stateMachine.SubmitAsync(Signals.Restart.Instance, testProbe);
        testProbe.Expect<Signals.InvalidState>();
        
        // Then, expect no message for a little time while it does its simulated work
        testProbe.ExpectNoMessage(50);

        // Then, expect it to automatically transition to Done state
        testProbe.Expect<Signals.StateChanged>(500, x => x.State == "Done");
        
        // Then, expect it to transition back to Running state on Restart signal
        await stateMachine.SubmitAsync(Signals.Restart.Instance, testProbe);
        testProbe.Expect<Signals.StateChanged>(500, x => x.State == "Running");
        
        // And finally to transition to Done state.
        testProbe.Expect<Signals.StateChanged>(500, x => x.State == "Done");
    }
    
    private class TestStateMachine : DataBlock
    {
        private readonly WeakReference<ICanSubmit?> monitor;

        public TestStateMachine(ICanSubmit? monitor = null)
        {
            this.monitor = new(monitor);
            Ready();
        }

        private void NotifyStateChanged(string newState)
        {
            if (!monitor.TryGetTarget(out var target))
                return;
            _ = target.SubmitAsync(new Signals.StateChanged(newState), this);
        }

        private void Ready()
        {
            Receive<Signals.Start>(_ => Become(Running));
            ReceiveAny(_ => Sender?.SubmitAsync(Signals.InvalidState.Instance, this));
            
            NotifyStateChanged("Ready");
        }

        private void Running()
        {
            Receive<Signals.Finished>(_ => Become(Done));
            ReceiveAny(_ => Sender?.SubmitAsync(Signals.InvalidState.Instance, this));
            DataBlockScheduler.ScheduleTellOnce(this, 150, Signals.Finished.Instance, this);
            
            NotifyStateChanged("Running");
        }

        private void Done()
        {
            Receive<Signals.Restart>(_ => Become(Running));
            ReceiveAny(_ => Sender?.SubmitAsync(Signals.InvalidState.Instance, this));
            
            // Fizzle out after 1 second if no restart signal is received
            SetIdleTimeout(TimeSpan.FromSeconds(1));
            ReceiveTimeout(Stop);
            
            NotifyStateChanged("Done");
        }
    }

    private static class Signals
    {
        public class InvalidState
        {
            public static readonly InvalidState Instance = new();

            private InvalidState()
            {
            }
        }
        
        public class Start
        {
            public static readonly Start Instance = new();

            private Start()
            {
            }
        }
        
        public class Finished
        {
            public static readonly Finished Instance = new();

            private Finished() { }
        }
        
        public class Restart
        {
            public static readonly Restart Instance = new();

            private Restart() { }
        }

        public class StateChanged(string state)
        {
            public string State { get; } = state;
        }
    }
}