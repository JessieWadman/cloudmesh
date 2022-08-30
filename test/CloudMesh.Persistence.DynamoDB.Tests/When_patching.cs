using Amazon.DynamoDBv2.DataModel;

namespace CloudMesh.Persistence.DynamoDB.Tests
{
    public class When_patching
    {
        public class DemoDto
        {
            [DynamoDBHashKey]
            public Guid Id { get; init; }

            public string Name { get; init; }
            public string Address { get; init; }
            public int Version { get; init; }
            public List<string> List { get; init; } = new();
        }

        [Fact]
        public async Task PatchTest()
        {
            var repoFactory = new InMemoryRepositoryFactory();
            using var repo = repoFactory.For<DemoDto>("Demo");

            var item1 = new DemoDto
            {
                Id = Guid.NewGuid(),
                Name = "Test",
                Address = "Test 1",
                Version = 1
            };
            await repo.SaveAsync(item1, CancellationToken.None);

            await repo.Patch(item1.Id)
                .Set(i => i.Name, "Name changed")
                .If(i => i.Version, PatchCondition.Equals, 1)
                .With(new
                {
                    Address = "Address also changed"
                })
                .ExecuteAsync(CancellationToken.None);

            var item2 = await repo.GetById(item1.Id, CancellationToken.None);
            Assert.NotNull(item2);
            Assert.Equal("Name changed", item2.Name);
            Assert.Equal("Address also changed", item2.Address);
        }

        [Fact]
        public async Task IfSize_and_property_type_is_list()
        {
            var repoFactory = new InMemoryRepositoryFactory();
            using var repo = repoFactory.For<DemoDto>("Demo");

            var item1 = new DemoDto
            {
                Id = Guid.NewGuid(),
                Name = "Test",
                Address = "Test 1",
                Version = 1,
                List = new List<string>()
                {
                    "Item1",
                    "Item2",
                    "Item3"
                }

            };
            await repo.SaveAsync(item1, CancellationToken.None);

            await repo.Patch(item1.Id)
                .IfSize(i => i.List, PatchCondition.LessThan, 5)
                .Set(i => i.Name, "Name changed")
                .ExecuteAsync(CancellationToken.None);

            var item2 = await repo.GetById(item1.Id, CancellationToken.None);
            Assert.NotNull(item2);
            Assert.Equal("Name changed", item2.Name);

            await repo.Patch(item1.Id)
                .IfSize(i => i.List, PatchCondition.LessThan, 3)
                .Set(i => i.Address, "Address changed")
                .ExecuteAsync(CancellationToken.None);

            var item3 = await repo.GetById(item1.Id, CancellationToken.None);
            Assert.NotNull(item3);
            Assert.Equal("Test 1", item3.Address);
        }

        [Fact]
        public async Task IfSize_and_property_type_is_string()
        {
            var repoFactory = new InMemoryRepositoryFactory();
            using var repo = repoFactory.For<DemoDto>("Demo");

            var item1 = new DemoDto
            {
                Id = Guid.NewGuid(),
                Name = "Test",
                Address = "Test 1",
                Version = 1
            };
            await repo.SaveAsync(item1, CancellationToken.None);

            await repo.Patch(item1.Id)
                .IfSize(i => i.Name, PatchCondition.Equals, 4)
                .Set(i => i.Name, "Name changed")
                .ExecuteAsync(CancellationToken.None);

            var item2 = await repo.GetById(item1.Id, CancellationToken.None);
            Assert.NotNull(item2);
            Assert.Equal("Name changed", item2.Name);

            await repo.Patch(item1.Id)
               .IfSize(i => i.Address, PatchCondition.LessThan, 3)
               .Set(i => i.Address, "Address changed")
               .ExecuteAsync(CancellationToken.None);

            var item3 = await repo.GetById(item1.Id, CancellationToken.None);
            Assert.NotNull(item3);
            Assert.Equal("Test 1", item3.Address);
        }
    }
}
