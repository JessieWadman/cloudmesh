using Amazon.DynamoDBv2.DataModel;
using CloudMesh.Persistence.DynamoDB.Converters;

namespace CloudMesh.Persistence.DynamoDB.Tests
{
    public record IdOnly
    {
        public string? Id { get; init; }
    }

    public enum TestEnum
    {
        One,
        Two,
        Three
    }

    public class TestDto
    {
        [DynamoDBHashKey]
        [DynamoDBGlobalSecondaryIndexRangeKey("byParent")]
        public string? Id { get; set; }

        [DynamoDBGlobalSecondaryIndexHashKey("byParent")]
        public string? ParentId { get; set; }

        public string? Description { get; set; }

        [DynamoDBRangeKey(Converter = typeof(DynamoDBEnumToStringConverter<TestEnum>))]
        public TestEnum? Enum1 { get; set; }

        public TestEnum? Enum2 { get; set; }
    }

    public class TestSetDto
    {
        [DynamoDBHashKey]
        public string? Id { get; set; }

        public List<string> ParentIds { get; set; } = new();

        public string? Description { get; set; }
    }

    public enum TestEnumSortKey
    {
        Draft,
        Published,
        Active,
        Suspended
    }

    public class TestEnumSortKeyDto
    {
        [DynamoDBHashKey]
        public string? Id { get; set; }

        [DynamoDBRangeKey(Converter = typeof(DynamoDBEnumToStringConverter<TestEnum>))]
        public TestEnumSortKey SortKey { get; set; }

        public string? Description { get; set; }
    }

    public class TestWithNumericalIDDto
    {
        [DynamoDBHashKey]
        [DynamoDBGlobalSecondaryIndexRangeKey("byParent")]
        public int Id { get; set; }

        [DynamoDBGlobalSecondaryIndexHashKey("byParent")]
        public int ParentId { get; set; }

        public string? Description { get; set; }
    }
}
