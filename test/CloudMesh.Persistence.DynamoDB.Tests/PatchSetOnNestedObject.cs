using Amazon.DynamoDBv2.DataModel;
using CloudMesh.Persistence.DynamoDB;
using CloudMesh.Persistence.DynamoDB.Helpers;
using System.Linq.Expressions;

namespace Persistence.DynamoDB.Tests
{
    public class PatchSetOnNestedObject
    {
        public class Manager
        {
            [DynamoDBProperty(AttributeName = "DName")]
            public string? DisplayName { get; set; }
        }

        public class Employment
        {
            [DynamoDBProperty(AttributeName = "Mgr")]
            public Manager Manager { get; set; } = new();
        }

        public class Employee
        {
            [DynamoDBProperty(AttributeName = "Emps")]
            public List<Employment> Employments { get; set; } = new();
        }


        [Fact]
        public void DotNotationShouldWork()
        {
            Expression<Func<Employee, string?>> expr = e => e.Employments[0].Manager.DisplayName;
            var (propertyPath, _) = ExpressionHelper.GetDotNotation(expr);
            var dynamoDbExpression = ExpressionHelper.DotNotationToDynamoDBExpression<Employee>(propertyPath);
            Assert.Equal("Emps[0].Mgr.DName", dynamoDbExpression);
        }

        [Fact]
        public void ListAttributeShouldWork()
        {
            var employments = new Employment[]
            {
                new()
                {
                    Manager = new() { DisplayName = "Joe" }
                },
                new()
                {
                    Manager = new() { DisplayName = "Jane" }
                }
            };
            var listMap = AttributeHelper.ToAttributeValue(employments, typeof(Employee).GetProperty(nameof(Employee.Employments))!);
            Assert.NotNull(listMap);
        }
    }
}
