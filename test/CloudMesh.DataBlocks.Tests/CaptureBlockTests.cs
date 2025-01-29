namespace CloudMesh.DataBlocks.Tests;

public class CaptureBlockTests
{
    [Fact]
    public async Task WhenExpectedResultIsReceived()
    {
        await using var testProbe = new CaptureBlock();
        await using var worker = new Forwarder(testProbe);
        
        DataBlockScheduler.ScheduleTellOnce(worker, 100, "Hello", null);
        
        var result = testProbe.Expect<string>();
        Assert.Equal("Hello", result);
    }
    
    [Fact]
    public async Task WhenWrongResultIsReceived()
    {
        await using var testProbe = new CaptureBlock();
        await using var worker = new Forwarder(testProbe);
        
        DataBlockScheduler.ScheduleTellOnce(worker, 100, "Hello", null);

        Assert.Throws<InvalidOperationException>(() => testProbe.Expect<int>());
    }
    
    [Fact]
    public async Task WhenNoMessageShouldBeReceived()
    {
        await using var testProbe = new CaptureBlock();
        testProbe.ExpectNoMessage();
    }
    
    [Fact]
    public async Task WhenNoMessageIsExpectedButReceived()
    {
        await using var testProbe = new CaptureBlock();
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