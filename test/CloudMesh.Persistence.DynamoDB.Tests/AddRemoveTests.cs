namespace CloudMesh.Persistence.DynamoDB.Tests
{
    public class AddRemoveTests
    {
        [Fact]
        public async Task Save_and_delete_should_work()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using var repo = repositoryFactory.For<TestDto>("Test");

            await repo.SaveAsync(new TestDto { Id = "11", ParentId = null, Description = "Description1" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "12", ParentId = "11", Description = "Description2" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "23", ParentId = "11", Description = "Description3" }, CancellationToken.None);
            var item = new TestDto { Id = "24", ParentId = "21", Description = "Description4" };
            await repo.SaveAsync(item, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "25", ParentId = "21", Description = "Description5" }, CancellationToken.None);

            await repo.DeleteAsync("23", CancellationToken.None);

            var count = await repo
                .Scan()
                .ToAsyncEnumerable(CancellationToken.None)
                .CountAsync();

            Assert.Equal(4, count);

            await repo.DeleteAsync(CancellationToken.None, item);

            count = await repo
                .Scan()
                .ToAsyncEnumerable(CancellationToken.None)
                .CountAsync();

            Assert.Equal(3, count);
        }

        [Fact]
        public async Task Batch_writes_should_work()
        {
            var repositoryFactory = new InMemoryRepositoryFactory();
            using var repo = repositoryFactory.For<TestDto>("Test");

            await repo.SaveAsync(new TestDto { Id = "11", ParentId = null, Description = "Description1" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "12", ParentId = "11", Description = "Description2" }, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "23", ParentId = "11", Description = "Description3" }, CancellationToken.None);
            var item = new TestDto { Id = "24", ParentId = "21", Description = "Description4" };
            await repo.SaveAsync(item, CancellationToken.None);
            await repo.SaveAsync(new TestDto { Id = "25", ParentId = "21", Description = "Description5" }, CancellationToken.None);

            await repo.BatchWrite()
                .Delete(item)
                .Save(new TestDto { Id = "33", ParentId = "21", Description = "Description6" })
                .ExecuteAsync(CancellationToken.None);

            var actual = await repo
                .Scan()
                .ToAsyncEnumerable(CancellationToken.None)
                .ToArrayAsync();

            Assert.Equal(5, actual.Length);
            Assert.Equal("11", actual[0].Id);
            Assert.Equal("12", actual[1].Id);
            Assert.Equal("23", actual[2].Id);
            Assert.Equal("25", actual[3].Id);
            Assert.Equal("33", actual[4].Id);
        }
    }
}
