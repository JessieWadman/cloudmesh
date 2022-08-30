namespace CloudMesh.Persistence.DynamoDB.Tests
{
    public class When_converting_to_attribute_value
    {
        [Fact]
        public void Complex_objects_should_work()
        {
            var entity = new
            {
                Test = new
                {
                    StringList = new string[] { "Testing", "Everything" },
                    Objectarr = new[]
                    {
                        new { A = 1, B = "Test" },
                        new { A = 2, B = "Test2" }
                    }
                }
            };

            var attribute = AttributeHelper.ToAttributeValue(entity.Test, entity.GetType().GetProperty(nameof(entity.Test)));
            Assert.NotNull(attribute);
        }
    }
}
