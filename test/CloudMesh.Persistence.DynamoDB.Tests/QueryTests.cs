namespace CloudMesh.Persistence.DynamoDB.Tests
{
    public class QueryTests
    {
        [Fact]
        public async Task Default_index_should_work()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using var repo = repositoryFactory.For<TestDto>("Test");

            await repo.SaveAsync(new TestDto { Id = "1", ParentId = "null", Description = "Description1" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "2", ParentId = "1", Description = "Description2" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "3", ParentId = "1", Description = "Description3" }, CancellationToken.None);

            var first = await repo.Query()
                .WithHashKey("1")
                .ToAsyncEnumerable(CancellationToken.None)
                .FirstOrDefaultAsync();

            Assert.NotNull(first);
            Assert.Equal("1", first.Id);

            var shouldOnlyContainOne = await repo.Query()
                .WithHashKey("2")
                .ToArrayAsync(CancellationToken.None);

            Assert.Single(shouldOnlyContainOne);
            Assert.Equal("2", shouldOnlyContainOne[0].Id);
        }

        [Fact]
        public async Task SecondaryIndexes_should_work()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using var repo = repositoryFactory.For<TestDto>("Test");

            await repo.SaveAsync(new TestDto { Id = "1", ParentId = "null", Description = "Description1" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "2", ParentId = "1", Description = "Description2" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "3", ParentId = "1", Description = "Description3" }, CancellationToken.None);

            var first = await repo.Query()
                .WithHashKey("1")
                .ToAsyncEnumerable(CancellationToken.None)
                .FirstOrDefaultAsync();

            var shouldContainTwo = await repo.Query()
                .UseIndex("byParent")
                .WithHashKey("1")
                .ToArrayAsync(CancellationToken.None);

            Assert.Equal(2, shouldContainTwo.Length);
        }

        [Fact]
        public async Task WithSortKey_equals_should_work()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using var repo = repositoryFactory.For<TestDto>("Test");

            await repo.SaveAsync(new TestDto { Id = "1", ParentId = "null", Description = "Description1" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "2", ParentId = "1", Description = "Description2" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "3", ParentId = "1", Description = "Description3" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "4", ParentId = "1", Description = "Description4" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "5", ParentId = "1", Description = "Description5" }, CancellationToken.None);

            var first = await repo.Query()
                .WithHashKey("1")
                .ToAsyncEnumerable(CancellationToken.None)
                .FirstOrDefaultAsync();

            var shouldContainOne = await repo.Query()
                .UseIndex("byParent")
                .WithHashKey("1")
                .WithSortKey(QueryOperator.Equal, "3")
                .ToArrayAsync(CancellationToken.None);

            Assert.Single(shouldContainOne);
        }

        [Fact]
        public async Task WithSortKey_as_enum_equals_should_work()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using var repo = repositoryFactory.For<TestEnumSortKeyDto>("Test");

            await repo.SaveAsync(new TestEnumSortKeyDto { Id = "1", SortKey = TestEnumSortKey.Active, Description = "1:Active" }, CancellationToken.None);
            await repo.SaveAsync(new TestEnumSortKeyDto { Id = "1", SortKey = TestEnumSortKey.Suspended, Description = "1:Suspended" }, CancellationToken.None);
            await repo.SaveAsync(new TestEnumSortKeyDto { Id = "1", SortKey = TestEnumSortKey.Draft, Description = "1:Draft" }, CancellationToken.None);
            await repo.SaveAsync(new TestEnumSortKeyDto { Id = "4", SortKey = TestEnumSortKey.Active, Description = "Description4" }, CancellationToken.None);
            await repo.SaveAsync(new TestEnumSortKeyDto { Id = "5", SortKey = TestEnumSortKey.Active, Description = "Description5" }, CancellationToken.None);

            var shouldContainOne = await repo.Query()
                .WithHashKey("1")
                .WithSortKey(QueryOperator.Equal, TestEnumSortKey.Active)
                .ToAsyncEnumerable(CancellationToken.None)
                .FirstOrDefaultAsync();

            Assert.NotNull(shouldContainOne);
            Assert.Equal("1:Active", shouldContainOne.Description);
        }

        [Fact]
        public async Task WithSortKey_greater_than_should_work()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using var repo = repositoryFactory.For<TestWithNumericalIDDto>("Test");

            await repo.SaveAsync(new TestWithNumericalIDDto { Id = 1, ParentId = 0, Description = "Description1" }, CancellationToken.None);
            await repo.SaveAsync(new TestWithNumericalIDDto { Id = 2, ParentId = 1, Description = "Description2" }, CancellationToken.None);
            await repo.SaveAsync(new TestWithNumericalIDDto { Id = 3, ParentId = 1, Description = "Description3" }, CancellationToken.None);
            await repo.SaveAsync(new TestWithNumericalIDDto { Id = 4, ParentId = 1, Description = "Description4" }, CancellationToken.None);
            await repo.SaveAsync(new TestWithNumericalIDDto { Id = 5, ParentId = 1, Description = "Description5" }, CancellationToken.None);

            var actual = await repo.Query()
                .UseIndex("byParent")
                .WithHashKey(1)
                .WithSortKey(QueryOperator.GreaterThan, 3)
                .ToArrayAsync(CancellationToken.None);

            Assert.Equal(2, actual.Length);
            Assert.Equal(4, actual[0].Id);
            Assert.Equal(5, actual[1].Id);
        }

        [Fact]
        public async Task Reversal_should_work()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using var repo = repositoryFactory.For<TestWithNumericalIDDto>("Test");

            await repo.SaveAsync(new TestWithNumericalIDDto { Id = 1, ParentId = 0, Description = "Description1" }, CancellationToken.None);
            await repo.SaveAsync(new TestWithNumericalIDDto { Id = 2, ParentId = 1, Description = "Description2" }, CancellationToken.None);
            await repo.SaveAsync(new TestWithNumericalIDDto { Id = 3, ParentId = 1, Description = "Description3" }, CancellationToken.None);
            await repo.SaveAsync(new TestWithNumericalIDDto { Id = 4, ParentId = 1, Description = "Description4" }, CancellationToken.None);
            await repo.SaveAsync(new TestWithNumericalIDDto { Id = 5, ParentId = 1, Description = "Description5" }, CancellationToken.None);

            var actual = await repo.Query()
                .UseIndex("byParent")
                .WithHashKey(1)
                .WithSortKey(QueryOperator.GreaterThan, 3)
                .Reverse()
                .ToArrayAsync(CancellationToken.None);

            Assert.Equal(2, actual.Length);
            Assert.Equal(5, actual[0].Id);
            Assert.Equal(4, actual[1].Id);
        }

        [Fact]
        public async Task WithSortKey_greater_than_or_equal_should_work()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using var repo = repositoryFactory.For<TestWithNumericalIDDto>("Test");

            await repo.SaveAsync(new TestWithNumericalIDDto { Id = 1, ParentId = 0, Description = "Description1" }, CancellationToken.None);
            await repo.SaveAsync(new TestWithNumericalIDDto { Id = 2, ParentId = 1, Description = "Description2" }, CancellationToken.None);
            await repo.SaveAsync(new TestWithNumericalIDDto { Id = 3, ParentId = 1, Description = "Description3" }, CancellationToken.None);
            await repo.SaveAsync(new TestWithNumericalIDDto { Id = 4, ParentId = 1, Description = "Description4" }, CancellationToken.None);
            await repo.SaveAsync(new TestWithNumericalIDDto { Id = 5, ParentId = 1, Description = "Description5" }, CancellationToken.None);

            var actual = await repo.Query()
                .UseIndex("byParent")
                .WithHashKey(1)
                .WithSortKey(QueryOperator.GreaterThanOrEqual, 3)
                .ToArrayAsync(CancellationToken.None);

            Assert.Equal(3, actual.Length);
            Assert.Equal(3, actual[0].Id);
            Assert.Equal(4, actual[1].Id);
            Assert.Equal(5, actual[2].Id);
        }

        [Fact]
        public async Task WithSortKey_less_than_should_work()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using var repo = repositoryFactory.For<TestWithNumericalIDDto>("Test");

            await repo.SaveAsync(new TestWithNumericalIDDto { Id = 1, ParentId = 0, Description = "Description1" }, CancellationToken.None);
            await repo.SaveAsync(new TestWithNumericalIDDto { Id = 2, ParentId = 1, Description = "Description2" }, CancellationToken.None);
            await repo.SaveAsync(new TestWithNumericalIDDto { Id = 3, ParentId = 1, Description = "Description3" }, CancellationToken.None);
            await repo.SaveAsync(new TestWithNumericalIDDto { Id = 4, ParentId = 1, Description = "Description4" }, CancellationToken.None);
            await repo.SaveAsync(new TestWithNumericalIDDto { Id = 5, ParentId = 1, Description = "Description5" }, CancellationToken.None);

            var actual = await repo.Query()
                .UseIndex("byParent")
                .WithHashKey(1)
                .WithSortKey(QueryOperator.LessThan, 4)
                .ToArrayAsync(CancellationToken.None);

            Assert.Equal(2, actual.Length);
            Assert.Equal(2, actual[0].Id);
            Assert.Equal(3, actual[1].Id);
        }

        [Fact]
        public async Task WithSortKey_less_than_as_string_should_work()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using var repo = repositoryFactory.For<TestDto>("Test");

            await repo.SaveAsync(new TestDto { Id = "1", ParentId = "null", Description = "Description1" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "2", ParentId = "1", Description = "Description2" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "3", ParentId = "1", Description = "Description3" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "4", ParentId = "1", Description = "Description4" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "5", ParentId = "1", Description = "Description5" }, CancellationToken.None);

            var actual = await repo.Query()
                .UseIndex("byParent")
                .WithHashKey("1")
                .WithSortKey(QueryOperator.LessThan, "4")
                .ToArrayAsync(CancellationToken.None);

            Assert.Equal(2, actual.Length);
            Assert.Equal("2", actual[0].Id);
            Assert.Equal("3", actual[1].Id);
        }

        [Fact]
        public async Task WithSortKey_less_than_or_equal_should_work()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using var repo = repositoryFactory.For<TestWithNumericalIDDto>("Test");

            await repo.SaveAsync(new TestWithNumericalIDDto { Id = 1, ParentId = 0, Description = "Description1" }, CancellationToken.None);
            await repo.SaveAsync(new TestWithNumericalIDDto { Id = 2, ParentId = 1, Description = "Description2" }, CancellationToken.None);
            await repo.SaveAsync(new TestWithNumericalIDDto { Id = 3, ParentId = 1, Description = "Description3" }, CancellationToken.None);
            await repo.SaveAsync(new TestWithNumericalIDDto { Id = 4, ParentId = 1, Description = "Description4" }, CancellationToken.None);
            await repo.SaveAsync(new TestWithNumericalIDDto { Id = 5, ParentId = 1, Description = "Description5" }, CancellationToken.None);

            var actual = await repo.Query()
                .UseIndex("byParent")
                .WithHashKey(1)
                .WithSortKey(QueryOperator.LessThanOrEqual, 4)
                .ToArrayAsync(CancellationToken.None);

            Assert.Equal(3, actual.Length);
            Assert.Equal(2, actual[0].Id);
            Assert.Equal(3, actual[1].Id);
            Assert.Equal(4, actual[2].Id);
        }

        [Fact]
        public async Task WithSortKey_begins_with_should_work()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using var repo = repositoryFactory.For<TestDto>("Test");

            await repo.SaveAsync(new TestDto { Id = "11", ParentId = "null", Description = "Description1" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "12", ParentId = "11", Description = "Description2" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "23", ParentId = "11", Description = "Description3" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "24", ParentId = "21", Description = "Description4" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "25", ParentId = "21", Description = "Description5" }, CancellationToken.None);

            var actual = await repo.Query()
                .UseIndex("byParent")
                .WithHashKey("11")
                .WithSortKey(QueryOperator.BeginsWith, "1")
                .ToArrayAsync(CancellationToken.None);

            Assert.Single(actual);
            Assert.Equal("12", actual[0].Id);
        }

        [Fact]
        public async Task WithSortKey_between_should_work()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using var repo = repositoryFactory.For<TestDto>("Test");

            await repo.SaveAsync(new TestDto { Id = "11", ParentId = "null", Description = "Description1" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "12", ParentId = "11", Description = "Description2" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "23", ParentId = "11", Description = "Description3" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "24", ParentId = "21", Description = "Description4" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "25", ParentId = "21", Description = "Description5" }, CancellationToken.None);

            var actual = await repo.Query()
                .UseIndex("byParent")
                .WithHashKey("11")
                .WithSortKey(QueryOperator.BeginsWith, "1")
                .ToArrayAsync(CancellationToken.None);

            Assert.Single(actual);
            Assert.Equal("12", actual[0].Id);
        }
    }
}
