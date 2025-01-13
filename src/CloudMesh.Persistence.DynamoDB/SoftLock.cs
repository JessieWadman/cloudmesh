using CloudMesh.Persistence.DynamoDB.Builders;
using System.Linq.Expressions;

namespace CloudMesh.Persistence.DynamoDB
{
    public static class SoftLock
    {
        private static async ValueTask<(bool Success, T? Item)> TryLockInternal<T>(
            this IRepository<T> repository,
            DynamoDBValue hashKey, 
            DynamoDBValue? rangeKey,
            Expression<Func<T, long>> lockUntilProperty,
            TimeProvider timeProvider,
            TimeSpan lockDuration,
            CancellationToken cancellationToken)
        {
            var rightNow = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
                
            if (lockDuration == default)
                lockDuration = TimeSpan.FromMinutes(5);

            var newLockWillExpireAt = timeProvider.GetUtcNow().Add(lockDuration).ToUnixTimeMilliseconds();

            var patch = rangeKey is null 
                ? repository.Patch(hashKey) 
                : repository.Patch(hashKey, rangeKey.Value);

            var item = await patch
                .If(lockUntilProperty, PatchCondition.LessThan, rightNow)
                .Set(lockUntilProperty, newLockWillExpireAt)
                .ExecuteAndGetAsync(cancellationToken);

            if (item is null)
                return (false, default);
            return (true, item);
        }

        public static ValueTask<(bool Success, T? Item)> TryLock<T>(
            this IRepository<T> repository,
            DynamoDBValue hashKey,
            DynamoDBValue rangeKey,
            Expression<Func<T, long>> lockUntilProperty,
            TimeProvider timeProvider,
            TimeSpan lockDuration,
            CancellationToken cancellationToken)
            => TryLockInternal(repository, hashKey, rangeKey, lockUntilProperty, timeProvider, lockDuration, cancellationToken);

        public static ValueTask<(bool Success, T? Item)> TryLock<T>(
            this IRepository<T> repository,
            DynamoDBValue hashKey,
            Expression<Func<T, long>> lockUntilProperty,
            TimeProvider timeProvider,
            TimeSpan lockDuration,
            CancellationToken cancellationToken)
            => TryLockInternal(repository, hashKey, null, lockUntilProperty, timeProvider, lockDuration, cancellationToken);
            
    }
}
