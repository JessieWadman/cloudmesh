using Amazon.DynamoDBv2.DataModel;
using CloudMesh.Persistence.DynamoDB.Builders;
using Moq;

namespace CloudMesh.Persistence.DynamoDB.Tests
{
    public class ScanTests
    {
        [Fact]
        public async Task Real_Scan_Builder_Enum_Filter_Being_Passed()
        {
            var dynamoDbContextMock = new Mock<IDynamoDBContext>();
            var scanBuilder = new ScanBuilder<TestDto>(dynamoDbContextMock.Object, () => new ScanConfig());

            try
            {
                var result = await scanBuilder
                    .Where(testRec => testRec.Enum1, ScanOperator.Equal, TestEnum.One)
                    .Where(testRec => testRec.Enum2, ScanOperator.Equal, TestEnum.Two)
                    .ToArrayAsync(CancellationToken.None);
            }
            catch
            {
                // This will trow as expected because we cannot mock ScanResponse
                // TODO try this approach https://stackoverflow.com/questions/56845357/how-to-fake-scanresponse
            }

            dynamoDbContextMock.Verify(ctx => ctx.ScanAsync<TestDto>(
                It.Is<ScanCondition[]>(conditions =>
                    conditions.Single(cond => cond.PropertyName == "Enum1" && cond.Values[0].GetType().Equals(typeof(TestEnum))) != null),
                It.IsAny<ScanConfig>()), Times.Once);
            dynamoDbContextMock.Verify(ctx => ctx.ScanAsync<TestDto>(
                It.Is<ScanCondition[]>(conditions =>
                    conditions.Single(cond => cond.PropertyName == "Enum2" && cond.Values[0].GetType().Equals(typeof(TestEnum))) != null),
                It.IsAny<ScanConfig>()), Times.Once);
        }

        [Fact]
        public async Task No_filtering_should_work()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using var repo = repositoryFactory.For<TestDto>("Test");

            await repo.SaveAsync(new TestDto { Id = "1", ParentId = "null", Description = "Description1" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "2", ParentId = "1", Description = "Description2" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "3", ParentId = "1", Description = "Description3" }, CancellationToken.None);

            var actual = await repo
                .Scan()
                .ToArrayAsync(CancellationToken.None);

            Assert.Equal(3, actual.Length);                  
        }

        [Fact]
        public async Task Simple_where_should_work()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using var repo = repositoryFactory.For<TestDto>("Test");

            await repo.SaveAsync(new TestDto { Id = "1", ParentId = "null", Description = "Description1" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "2", ParentId = "1", Description = "Description2" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "3", ParentId = "1", Description = "Description3" }, CancellationToken.None);

            var actual = await repo.Scan()
                .Where(t => t.Description, ScanOperator.Equal, "Description2")
                .ToArrayAsync(CancellationToken.None);

            Assert.Single(actual);
            Assert.Equal("2", actual[0].Id);

            var shouldOnlyContainOne = await repo.Query()
                .WithHashKey("2")
                .ToArrayAsync(CancellationToken.None);

            Assert.Single(shouldOnlyContainOne);
            Assert.Equal("2", shouldOnlyContainOne[0].Id);
        }

        [Fact]
        public async Task Multiple_where_should_work()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using var repo = repositoryFactory.For<TestDto>("Test");

            await repo.SaveAsync(new TestDto { Id = "1", ParentId = "null", Description = "Description1" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "2", ParentId = "1", Description = "Description2" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "3", ParentId = "1", Description = "Description3" }, CancellationToken.None);

            var actual = await repo.Scan()
                .UseIndex("byParent")
                .Where(d => d.ParentId, ScanOperator.Equal, "1")
                .Where(d => d.Description, ScanOperator.Equal, "Description2")
                .ToArrayAsync(CancellationToken.None);

            Assert.Single(actual);
            Assert.Equal("2", actual[0].Id);
        }

        [Fact]
        public async Task OrInsteadOfAnd_should_work()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using var repo = repositoryFactory.For<TestDto>("Test");

            await repo.SaveAsync(new TestDto { Id = "1", ParentId = "null", Description = "Description1" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "2", ParentId = "1", Description = "Description2" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "3", ParentId = "1", Description = "Description3" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "4", ParentId = "1", Description = "Description4" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "5", ParentId = "1", Description = "Description5" }, CancellationToken.None);

            var actual = await repo.Scan()
                .Where(a => a.Id, ScanOperator.Equal, "1")
                .Where(a => a.Id, ScanOperator.Equal, "3")
                .UseOrInsteadOfAnd()
                .ToArrayAsync(CancellationToken.None);
            
            Assert.Equal(2, actual.Length);
            Assert.Equal("1", actual[0].Id);
            Assert.Equal("3", actual[1].Id);

        }

        [Fact]
        public async Task Where_greater_than_or_equal_should_work()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using var repo = repositoryFactory.For<TestDto>("Test");

            await repo.SaveAsync(new TestDto { Id = "1", ParentId = "null", Description = "Description1" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "2", ParentId = "1", Description = "Description2" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "3", ParentId = "1", Description = "Description3" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "4", ParentId = "1", Description = "Description4" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "5", ParentId = "1", Description = "Description5" }, CancellationToken.None);

            var actual = await repo.Scan()
                .UseIndex("byParent")
                .Where(a => a.Id, ScanOperator.GreaterThanOrEqual, "3")
                .ToArrayAsync(CancellationToken.None);

            Assert.Equal(3, actual.Length);
            Assert.Equal("3", actual[0].Id);
            Assert.Equal("4", actual[1].Id);
            Assert.Equal("5", actual[2].Id);
        }

        [Fact]
        public async Task Where_less_than_should_work()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using var repo = repositoryFactory.For<TestDto>("Test");

            await repo.SaveAsync(new TestDto { Id = "1", ParentId = "null", Description = "Description1" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "2", ParentId = "1", Description = "Description2" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "3", ParentId = "1", Description = "Description3" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "4", ParentId = "1", Description = "Description4" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "5", ParentId = "1", Description = "Description5" }, CancellationToken.None);

            var actual = await repo.Scan()
                .UseIndex("byParent")
                .Where(a => a.Id, ScanOperator.LessThan, "4")
                .ToArrayAsync(CancellationToken.None);

            Assert.Equal(3, actual.Length);
            Assert.Equal("1", actual[0].Id);
            Assert.Equal("2", actual[1].Id);
            Assert.Equal("3", actual[2].Id);
        }

        [Fact]
        public async Task Where_begins_with_should_work()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using var repo = repositoryFactory.For<TestDto>("Test");

            await repo.SaveAsync(new TestDto { Id = "1", ParentId = "null", Description = "Description1" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "2", ParentId = "1", Description = "Description2" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "3", ParentId = "1", Description = "Description3" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "4", ParentId = "1", Description = "Description4" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "5", ParentId = "1", Description = "Description5" }, CancellationToken.None);

            var actual = await repo.Scan()
                .UseIndex("byParent")
                .Where(a => a.Description, ScanOperator.BeginsWith, "Description")
                .ToArrayAsync(CancellationToken.None);

            Assert.Equal(5, actual.Length);
        }

        [Fact]
        public async Task Where_begins_with_should_be_case_sensitive()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using var repo = repositoryFactory.For<TestDto>("Test");

            await repo.SaveAsync(new TestDto { Id = "1", ParentId = "null", Description = "Description1" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "2", ParentId = "1", Description = "Description2" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "3", ParentId = "1", Description = "Description3" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "4", ParentId = "1", Description = "Description4" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "5", ParentId = "1", Description = "Description5" }, CancellationToken.None);

            var actual = await repo.Scan()
                .UseIndex("byParent")
                .Where(a => a.Description, ScanOperator.BeginsWith, "DESCRIPTION")
                .ToArrayAsync(CancellationToken.None);

            Assert.Empty(actual);
        }

        [Fact]
        public async Task Where_between_should_work()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using var repo = repositoryFactory.For<TestWithNumericalIDDto>("Test");

            await repo.SaveAsync(new TestWithNumericalIDDto { Id = 1, ParentId = 0, Description = "Description1" }, CancellationToken.None);
            await repo.SaveAsync(new TestWithNumericalIDDto { Id = 2, ParentId = 1, Description = "Description2" }, CancellationToken.None);
            await repo.SaveAsync(new TestWithNumericalIDDto { Id = 3, ParentId = 1, Description = "Description3" }, CancellationToken.None);
            await repo.SaveAsync(new TestWithNumericalIDDto { Id = 4, ParentId = 1, Description = "Description4" }, CancellationToken.None);
            await repo.SaveAsync(new TestWithNumericalIDDto { Id = 5, ParentId = 1, Description = "Description5" }, CancellationToken.None);

            var actual = await repo.Scan()
                .UseIndex("byParent")
                .Where(a => a.Id, ScanOperator.Between, 2, 4)
                .ToArrayAsync(CancellationToken.None);

            Assert.Equal(3, actual.Length);
            Assert.Equal(2, actual[0].Id);
            Assert.Equal(3, actual[1].Id);
            Assert.Equal(4, actual[2].Id);
        }

        [Fact]
        public async Task Where_between_on_string_should_work()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using var repo = repositoryFactory.For<TestDto>("Test");

            await repo.SaveAsync(new TestDto { Id = "1", ParentId = "null", Description = "Description1" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "2", ParentId = "1", Description = "Description2" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "3", ParentId = "1", Description = "Description3" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "4", ParentId = "1", Description = "Description4" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "5", ParentId = "1", Description = "Description5" }, CancellationToken.None);

            var actual = await repo.Scan()
                .UseIndex("byParent")
                .Where(a => a.Id, ScanOperator.Between, "2", "4")
                .ToArrayAsync(CancellationToken.None);

            Assert.Equal(3, actual.Length);
            Assert.Equal("2", actual[0].Id);
            Assert.Equal("3", actual[1].Id);
            Assert.Equal("4", actual[2].Id);
        }

        [Fact]
        public async Task Where_contains_should_work()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using var repo = repositoryFactory.For<TestDto>("Test");

            await repo.SaveAsync(new TestDto { Id = "11", ParentId = "null", Description = "Description1" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "12", ParentId = "11", Description = "Description2" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "23", ParentId = "11", Description = "Description3" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "24", ParentId = "21", Description = "Description4" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "25", ParentId = "21", Description = "Description5" }, CancellationToken.None);

            var actual = await repo.Scan()
                .UseIndex("byParent")
                .Where(a => a.Id, ScanOperator.Contains, "2")
                .ToArrayAsync(CancellationToken.None);

            Assert.Equal(4, actual.Length);
        }

        [Fact]
        public async Task Where_set_contains_should_work()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using var repo = repositoryFactory.For<TestSetDto>("Test");

            await repo.SaveAsync(new TestSetDto { Id = "11", ParentIds = new List<string>() { "null" }, Description = "Description1" }, CancellationToken.None);
            await repo.SaveAsync(new TestSetDto { Id = "12", ParentIds = new List<string>() { "11" }, Description = "Description2" }, CancellationToken.None);
            await repo.SaveAsync(new TestSetDto { Id = "23", ParentIds = new List<string>() { "11", "12" }, Description = "Description3" }, CancellationToken.None);
            await repo.SaveAsync(new TestSetDto { Id = "24", ParentIds = new List<string>() { "23", "11" }, Description = "Description4" }, CancellationToken.None);
            await repo.SaveAsync(new TestSetDto { Id = "25", ParentIds = new List<string>() { "25", "12", "23" }, Description = "Description5" }, CancellationToken.None);

            var actual = await repo.Scan()
                .Where(a => a.ParentIds, ScanOperator.Contains, "11")
                .ToArrayAsync(CancellationToken.None);

            Assert.Equal(3, actual.Length);
        }

        [Fact]
        public async Task Where_set_not_contains_should_work()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using var repo = repositoryFactory.For<TestSetDto>("Test");

            await repo.SaveAsync(new TestSetDto { Id = "11", ParentIds = new List<string>() { "null" }, Description = "Description1" }, CancellationToken.None);
            await repo.SaveAsync(new TestSetDto { Id = "12", ParentIds = new List<string>() { "11" }, Description = "Description2" }, CancellationToken.None);
            await repo.SaveAsync(new TestSetDto { Id = "23", ParentIds = new List<string>() { "11", "12" }, Description = "Description3" }, CancellationToken.None);
            await repo.SaveAsync(new TestSetDto { Id = "24", ParentIds = new List<string>() { "23", "11" }, Description = "Description4" }, CancellationToken.None);
            await repo.SaveAsync(new TestSetDto { Id = "25", ParentIds = new List<string>() { "25", "12", "23" }, Description = "Description5" }, CancellationToken.None);

            var actual = await repo.Scan()
                .Where(a => a.ParentIds, ScanOperator.NotContains, "11")
                .ToArrayAsync(CancellationToken.None);

            Assert.Equal(2, actual.Length);
        }

        [Fact]
        public async Task Where_not_contains_should_work()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using var repo = repositoryFactory.For<TestDto>("Test");

            await repo.SaveAsync(new TestDto { Id = "11", ParentId = "null", Description = "Description1" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "12", ParentId = "11", Description = "Description2" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "23", ParentId = "11", Description = "Description3" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "24", ParentId = "21", Description = "Description4" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "25", ParentId = "21", Description = "Description5" }, CancellationToken.None);

            var actual = await repo.Scan()
                .UseIndex("byParent")
                .Where(a => a.Id, ScanOperator.NotContains, "2")
                .ToArrayAsync(CancellationToken.None);

            Assert.Single(actual);
        }

        [Fact]
        public async Task Where_NotEqual_should_work()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using var repo = repositoryFactory.For<TestDto>("Test");

            await repo.SaveAsync(new TestDto { Id = "11", ParentId = "null", Description = "Description1" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "12", ParentId = "11", Description = "Description2" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "23", ParentId = "11", Description = "Description3" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "24", ParentId = "21", Description = "Description4" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "25", ParentId = "21", Description = "Description5" }, CancellationToken.None);

            var actual = await repo.Scan()
                .UseIndex("byParent")
                .Where(a => a.Id, ScanOperator.NotEqual, "23")
                .ToArrayAsync(CancellationToken.None);

            Assert.Equal(4, actual.Length);
        }

        [Fact]
        public async Task Where_IsNull_should_work()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using var repo = repositoryFactory.For<TestDto>("Test");

            await repo.SaveAsync(new TestDto { Id = "11", ParentId = null, Description = "Description1" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "12", ParentId = "11", Description = "Description2" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "23", ParentId = "11", Description = "Description3" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "24", ParentId = "21", Description = "Description4" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "25", ParentId = "21", Description = "Description5" }, CancellationToken.None);

            var actual = await repo.Scan()
                .UseIndex("byParent")
                .Where(a => a.ParentId, ScanOperator.IsNull)
                .ToArrayAsync(CancellationToken.None);

            Assert.Single(actual);
        }

        [Fact]
        public async Task Where_IsNotNull_should_work()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using var repo = repositoryFactory.For<TestDto>("Test");
            
            await repo.SaveAsync(new TestDto { Id = "11", ParentId = null, Description = "Description1" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "12", ParentId = "11", Description = "Description2" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "23", ParentId = "11", Description = "Description3" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "24", ParentId = "21", Description = "Description4" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "25", ParentId = "21", Description = "Description5" }, CancellationToken.None);

            var actual = await repo.Scan()
                .UseIndex("byParent")
                .Where(a => a.ParentId, ScanOperator.IsNotNull)
                .ToArrayAsync(CancellationToken.None);

            Assert.Equal(4, actual.Length);
        }

        [Fact]
        public async Task Where_In_should_work()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using var repo = repositoryFactory.For<TestDto>("Test");

            await repo.SaveAsync(new TestDto { Id = "11", ParentId = null, Description = "Description1" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "12", ParentId = "11", Description = "Description2" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "23", ParentId = "11", Description = "Description3" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "24", ParentId = "21", Description = "Description4" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "25", ParentId = "21", Description = "Description5" }, CancellationToken.None);

            var actual = await repo.Scan()
                .UseIndex("byParent")
                .Where(a => a.Id, ScanOperator.In, "11", "12", "23", "44")
                .ToArrayAsync(CancellationToken.None);

            Assert.Equal(3, actual.Length);
        }

        [Fact]
        public async Task Projection_should_work()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using (var fullDtoRepo = repositoryFactory.For<TestDto>("Test"))
            {
                await fullDtoRepo.SaveAsync(new TestDto { Id = "11", ParentId = null, Description = "Description1" }, CancellationToken.None);
                await fullDtoRepo.SaveAsync(new TestDto { Id = "12", ParentId = "11", Description = "Description2" }, CancellationToken.None);
                await fullDtoRepo.SaveAsync(new TestDto { Id = "23", ParentId = "11", Description = "Description3" }, CancellationToken.None);
                await fullDtoRepo.SaveAsync(new TestDto { Id = "24", ParentId = "21", Description = "Description4" }, CancellationToken.None);
                await fullDtoRepo.SaveAsync(new TestDto { Id = "25", ParentId = "21", Description = "Description5" }, CancellationToken.None);
            }

            using var projectionRepo = repositoryFactory.For<IdOnly>("Test");

            var actual = await projectionRepo.Scan()
                .Where(a => a.Id, ScanOperator.In, "11", "12", "23", "44")
                .ToArrayAsync(CancellationToken.None);

            Assert.Equal(3, actual.Length);
        }
    }
}
