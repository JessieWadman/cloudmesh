namespace CloudMesh.DataBlocks.Tests;

public class TetsProbeTests
{
    [Fact]
    public async Task WhenExpectedResultIsReceived()
    {
        var testProbe = new TestProbe();
        await using var worker = new Forwarder(testProbe);
        
        DataBlockScheduler.ScheduleTellOnce(worker, 100, "Hello", null);
        
        var result = testProbe.Expect<string>();
        Assert.Equal("Hello", result);
    }
    
    [Fact]
    public async Task WhenWrongResultIsReceived()
    {
        var testProbe = new TestProbe();
        await using var worker = new Forwarder(testProbe);
        
        DataBlockScheduler.ScheduleTellOnce(worker, 100, "Hello", null);

        Assert.Throws<InvalidOperationException>(() => testProbe.Expect<int>());
    }
    
    [Fact]
    public void WhenNoMessageShouldBeReceived()
    {
        var testProbe = new TestProbe();
        testProbe.ExpectNoMessage();
    }
    
    [Fact]
    public async Task WhenNoMessageIsExpectedButReceived()
    {
        var testProbe = new TestProbe();
        await using var worker = new Forwarder(testProbe);
        
        DataBlockScheduler.ScheduleTellOnce(worker, 100, "Hello", null);

        Assert.Throws<InvalidOperationException>(() => testProbe.ExpectNoMessage());
    }
    
    private class Forwarder : DataBlock
    {
        public Forwarder(ICanSubmit target)
        {
            ReceiveAnyAsync(msg => target.SubmitAsync(msg, Sender));
        }
    }
}