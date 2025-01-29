namespace CloudMesh.DataBlocks.Tests;

public class SchedulerTests
{
    [Fact]
    public void ScheduleTellOnceShouldWork()
    {
        // Note, the scheduler is NOT exact down to millisecond, it entirely depends on system load.
        // Consider the delay to be "Not before".
        var testProbe = new TestProbe();
        DataBlockScheduler.ScheduleTellOnce(testProbe, TimeSpan.FromMilliseconds(100), "Hello", null);
        
        testProbe.ExpectNoMessage(80); // No message within 80ms
        testProbe.Expect<string>(100); // After those 80ms, we should receive the message within 30ms
    }
}