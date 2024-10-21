using Xunit;

namespace CloudMesh.DotNotationHelper.Tests;

public class DotNotationTests
{
    public class TestClass
    {
        public Dictionary<string, string> SimpleDict { get; set; } = new();
        public Dictionary<int, List<string>> NestedDictList { get; set; } = new();
        public Dictionary<Guid, TestClass> GuidDict { get; set; } = new();
        public List<Dictionary<string, string>> ListOfDict { get; set; } = new();
    }

    [Fact]
    public void SetValue_SimpleDictionary_StringKey()
    {
        var obj = new TestClass();
        DotNotation.SetValue(obj, "SimpleDict[\"key1\"]", "value1");
        Assert.Equal("value1", obj.SimpleDict["key1"]);
    }

    [Fact]
    public void GetValue_SimpleDictionary_StringKey()
    {
        var obj = new TestClass();
        obj.SimpleDict["key1"] = "value1";
        var result = DotNotation.GetValue(obj, "SimpleDict[\"key1\"]");
        Assert.Equal("value1", result);
    }

    [Fact]
    public void SetValue_NestedDictionaryList_IntKey()
    {
        var obj = new TestClass();
        DotNotation.SetValue(obj, "NestedDictList[1][0]", "value1");
        Assert.Equal("value1", obj.NestedDictList[1][0]);
    }

    [Fact]
    public void GetValue_NestedDictionaryList_IntKey()
    {
        var obj = new TestClass();
        obj.NestedDictList[1] = new List<string> { "value1" };
        var result = DotNotation.GetValue(obj, "NestedDictList[1][0]");
        Assert.Equal("value1", result);
    }

    [Fact]
    public void SetValue_GuidDictionary()
    {
        var obj = new TestClass();
        var guid = Guid.NewGuid();
        DotNotation.SetValue(obj, $"GuidDict[{guid}].SimpleDict[\"key1\"]", "value1");
        Assert.Equal("value1", obj.GuidDict[guid].SimpleDict["key1"]);
    }

    [Fact]
    public void GetValue_GuidDictionary()
    {
        var obj = new TestClass();
        var guid = Guid.NewGuid();
        obj.GuidDict[guid] = new TestClass();
        obj.GuidDict[guid].SimpleDict["key1"] = "value1";
        var result = DotNotation.GetValue(obj, $"GuidDict[{guid}].SimpleDict[\"key1\"]");
        Assert.Equal("value1", result);
    }

    [Fact]
    public void SetValue_ListOfDictionaries()
    {
        var obj = new TestClass();
        DotNotation.SetValue(obj, "ListOfDict[0][\"key1\"]", "value1");
        Assert.Equal("value1", obj.ListOfDict[0]["key1"]);
    }

    [Fact]
    public void GetValue_ListOfDictionaries()
    {
        var obj = new TestClass();
        obj.ListOfDict.Add(new Dictionary<string, string> { { "key1", "value1" } });
        var result = DotNotation.GetValue(obj, "ListOfDict[0][\"key1\"]");
        Assert.Equal("value1", result);
    }

    [Fact]
    public void SetValue_MultiLevelDictionaryAndList()
    {
        var obj = new TestClass();
        DotNotation.SetValue(obj, "NestedDictList[1][2]", "value1");
        Assert.Equal("value1", obj.NestedDictList[1][2]);
        DotNotation.SetValue(obj, "NestedDictList[1][0]", "value2");
        Assert.Equal("value2", obj.NestedDictList[1][0]);

        var guid = Guid.NewGuid();
        DotNotation.SetValue(obj, $"GuidDict[{guid}].SimpleDict[\"key2\"]", "value3");
        Assert.Equal("value3", obj.GuidDict[guid].SimpleDict["key2"]);
    }

    [Fact]
    public void GetValue_MultiLevelDictionaryAndList()
    {
        var obj = new TestClass();
        obj.NestedDictList[1] = new List<string> { "value1" };
        var result1 = DotNotation.GetValue(obj, "NestedDictList[1][0]");
        Assert.Equal("value1", result1);

        var guid = Guid.NewGuid();
        obj.GuidDict[guid] = new TestClass();
        obj.GuidDict[guid].SimpleDict["key2"] = "value2";
        var result2 = DotNotation.GetValue(obj, $"GuidDict[{guid}].SimpleDict[\"key2\"]");
        Assert.Equal("value2", result2);
    }

    [Fact]
    public void SetValue_EnumDictionary()
    {
        var obj = new Dictionary<TestEnum, string>();
        DotNotation.SetValue(obj, "[First]", "value1");
        Assert.Equal("value1", obj[TestEnum.First]);
    }

    [Fact]
    public void GetValue_EnumDictionary()
    {
        var obj = new Dictionary<TestEnum, string> { { TestEnum.First, "value1" } };
        var result = DotNotation.GetValue(obj, "[First]");
        Assert.Equal("value1", result);
    }
}

public enum TestEnum
{
    First,
    Second
}