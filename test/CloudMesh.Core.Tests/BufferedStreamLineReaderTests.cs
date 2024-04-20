using System.Text;
using CloudMesh.IO;

namespace CloudMesh.Core.Tests;

public class BufferedStreamLineReaderTests
{
    [Fact]
    public async Task OneLinerShouldWork()
    {
        var contents = "Hello world"u8.ToArray();
        using var ms = new MemoryStream(contents);
        var reader = new BufferedStreamLineReader(ms);
        Assert.True(await reader.ReadAsync(default));
        Assert.True(reader.CurrentLineBytes.Length > 0);
        Assert.True(reader.CurrentLineBytes.Span.SequenceEqual(contents));
        Assert.False(await reader.ReadAsync(default));
    }
    
    [Fact]
    public async Task TwoLinerShouldWork()
    {
        var hello = "Hello"u8.ToArray();
        var world = "world"u8.ToArray();
        byte[] contents = [..hello, .."\n"u8.ToArray(), ..world];
        using var ms = new MemoryStream(contents);
        var reader = new BufferedStreamLineReader(ms);
        Assert.True(await reader.ReadAsync(default));
        Assert.True(reader.CurrentLineBytes.Length > 0);
        Assert.True(reader.CurrentLineBytes.Span.SequenceEqual(hello));
        
        Assert.True(await reader.ReadAsync(default));
        Assert.True(reader.CurrentLineBytes.Length > 0);
        Assert.True(reader.CurrentLineBytes.Span.SequenceEqual(world));
        
        Assert.False(await reader.ReadAsync(default));
    }
    
    [Fact]
    public async Task MultiLinerShouldWork()
    {
        var lines = Enumerable.Range(1, 10_000_000).Select(i => i.ToString());
        var contents = Encoding.UTF8.GetBytes(string.Join("\n", lines));
        
        using var ms = new MemoryStream(contents);
        var reader = new BufferedStreamLineReader(ms);
        foreach (var line in lines)
        {
            Assert.True(await reader.ReadAsync(default));
            Assert.True(reader.CurrentLineBytes.Length > 0);
            Assert.True(reader.CurrentLineBytes.Span.SequenceEqual(Encoding.UTF8.GetBytes(line)));
        }

        Assert.False(await reader.ReadAsync(default));
    }
    
}