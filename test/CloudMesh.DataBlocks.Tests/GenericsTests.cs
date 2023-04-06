namespace CloudMesh.DataBlocks.Tests;

public interface IGeneric
{
}

public class Solid : IGeneric
{
    
}

public class Receiver : DataBlock
{
    public Receiver(Action<bool> completion)
    {
        ReceiveAsync<IGeneric>(_ =>
        {
            completion(true);
            return ValueTask.CompletedTask;
        });
        
        ReceiveAnyAsync(_ =>
        {
            completion(false);
            return ValueTask.CompletedTask;
        });
    }
}

public class GenericsTests
{
    [Fact]
    public async Task ReceivingInterfacesShouldWork()
    {
        TaskCompletionSource<bool> completion = new();
        
        var receiver = new Receiver(x =>
        {
            completion.SetResult(x);
        });
        await receiver.SubmitAsync(new Solid(), null);
        var result = await completion.Task;
        Assert.True(result);
    }
}