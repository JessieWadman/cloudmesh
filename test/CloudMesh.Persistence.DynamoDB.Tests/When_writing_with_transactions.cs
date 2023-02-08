using Amazon.DynamoDBv2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloudMesh.Persistence.DynamoDB.Tests
{
    public class When_writing_with_transactions
    {
        public record Employee(int Id, string Name, int Flags, DateOnly DateOfBirth);

        public async Task Run_local_only()
        {
            using var client = new AmazonDynamoDBClient();
            var repoFactory = new DynamoDBRepositoryFactory(client);
            await repoFactory.Transaction()
                .Save("Employees", new Employee(1, "John Doe", 5, DateOnly.FromDateTime(DateTime.Today.AddYears(-25))))
                .Patch<Employee>("Employees", 1)
                    .If(e => e.Flags, Builders.PatchCondition.LessThan, 10)
                    .With(new { Name = "Jane Doe" })
                    .Build()
                .Delete("Employees", new { Id = 2 })
                .ExecuteAsync(default);
        }
    }
}
