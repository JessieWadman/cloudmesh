namespace CloudMesh.DataBlocks.Tests;

public class PipeToTests
{
    [Fact]
    public void PipeToShouldWork()
    {
        var testProbe = new TestProbe();

        // Defer execution of lengthy, async work, and pipe the result to the target test probe.
        LengthyStuff.DoSomeLengthyAsyncWork("Hello!").PipeTo(testProbe, null);

        var str = testProbe.Expect<string>();
        Assert.Equal("HELLO!", str);
    }

    [Fact]
    public void PipeToFailureShouldWork()
    {
        var testProbe = new TestProbe();

        LengthyStuff.DoAsyncWorkAndThrow().PipeTo(testProbe, null);

        var exception = testProbe.Expect<Exception>();

        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal("Boo!", exception.Message);
    }
    
    [Fact]
    public async Task PipeToBlockShouldWork()
    {
        var testProbe = new TestProbe();

        var worker = new PipeToTestBlock();
        await worker.SubmitAsync(new PipeToTestBlock.ReverseString("Hello!"), testProbe);

        var str = testProbe.Expect<string>();
        Assert.Equal("HELLO!", str);
    }
    
    private static class LengthyStuff
    {
        public static async Task<string> DoSomeLengthyAsyncWork(string value)
        {
            await Task.Delay(100);
            return value.ToUpper();
        }

        public static async Task<string> DoAsyncWorkAndThrow()
        {
            await Task.Delay(100);
            throw new InvalidOperationException("Boo!");
        }
    }

    private sealed class PipeToTestBlock : DataBlock
    {
        public PipeToTestBlock()
        {
            Receive<ReverseString>(msg => LengthyStuff.DoSomeLengthyAsyncWork(msg.Value).PipeTo(Sender!, this));
        }

        public sealed record ReverseString(string Value);
    }
}