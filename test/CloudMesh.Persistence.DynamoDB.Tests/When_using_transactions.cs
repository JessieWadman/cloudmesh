using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;

namespace CloudMesh.Persistence.DynamoDB.Tests
{
    public class When_using_transactions
    {
        public record TestRec
        {
            [DynamoDBHashKey]
            public string? Id { get; set; }
            public string? Name { get; set; }
        }

        public class And_transaction_is_committed
        {
            [Fact]
            public async Task Records_should_be_saved()
            {
                var repositoryFactory = new InMemoryRepositoryFactory();
                var success = await repositoryFactory.Transaction()
                    .Save("test1", new TestRec { Id = "42", Name = "test42" })
                    .Patch<TestRec>("test1", "1")
                        .If(rec => rec.Name, Builders.PatchCondition.NotEquals, "test1")
                        .With(new { Name = "test3" })
                        .Build()                    
                    .Save("test2", new TestRec { Id = "4", Name = "test44" })
                    .ExecuteAsync(default);
                Assert.True(success);

                var table1 = repositoryFactory.For<TestRec>("test1");
                var allRows = await table1.Scan().ToArrayAsync(default);
                Assert.Equal(2, allRows.Length);
            }
        }

        public class And_condition_is_not_met
        {
            [Fact]
            public async Task No_changes_should_be_made()
            {
                var repositoryFactory = new InMemoryRepositoryFactory();
                var success = await repositoryFactory.Transaction()
                    .Save("test1", new TestRec { Id = "42", Name = "test42" })
                    .Patch<TestRec>("test1", "1")
                        .If(rec => rec.Name, Builders.PatchCondition.Equals, "test1")
                        .With(new { Name = "test3" })
                        .Build()
                    .Save("test2", new TestRec { Id = "4", Name = "test44" })
                    .ExecuteAsync(default);
                Assert.False(success);

                var table1 = repositoryFactory.For<TestRec>("test1");
                var allRows = await table1.Scan().ToArrayAsync(default);
                Assert.Empty(allRows);

                var table2 = repositoryFactory.For<TestRec>("test2");
                allRows = await table1.Scan().ToArrayAsync(default);
                Assert.Empty(allRows);
            }
        }
    }
}
