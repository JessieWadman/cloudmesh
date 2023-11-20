using CloudMesh.Persistence.DynamoDB.Builders;
using System.Linq.Expressions;

#if (NET8_0_OR_GREATER)
#else
    using CloudMesh.Utils;
#endif  

namespace CloudMesh.Persistence.DynamoDB
{
    public static class SoftLock
    {
        private static async ValueTask<(bool Success, T? Item)> TryLockInternal<T>(
            this IRepository<T> repository,
            DynamoDBValue hashKey, 
            DynamoDBValue? rangeKey,
            Expression<Func<T, long>> lockUntilProperty,
#if (NET8_0_OR_GREATER)
            TimeProvider timeProvider,
#else
            ISystemClock systemClock,
#endif         
            
            TimeSpan lockDuration,
            CancellationToken cancellationToken)
        {
#if (NET8_0_OR_GREATER)
            var rightNow = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
#else
            var rightNow = systemClock.UtcNowWithOffset.ToUnixTimeMilliseconds();
#endif  
                
            if (lockDuration == default)
                lockDuration = TimeSpan.FromMinutes(5);

#if (NET8_0_OR_GREATER)
            var newLockWillExpireAt = timeProvider.GetUtcNow().Add(lockDuration).ToUnixTimeMilliseconds();
#else
            var newLockWillExpireAt = systemClock.UtcNowWithOffset.Add(lockDuration).ToUnixTimeMilliseconds();
#endif  
            

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
#if (NET8_0_OR_GREATER)
            TimeProvider timeProvider,
#else
            ISystemClock timeProvider,
#endif              
            TimeSpan lockDuration,
            CancellationToken cancellationToken)
            => TryLockInternal(repository, hashKey, rangeKey, lockUntilProperty, timeProvider, lockDuration, cancellationToken);

        public static ValueTask<(bool Success, T? Item)> TryLock<T>(
            this IRepository<T> repository,
            DynamoDBValue hashKey,
            Expression<Func<T, long>> lockUntilProperty,
#if (NET8_0_OR_GREATER)
            TimeProvider timeProvider,
#else
            ISystemClock timeProvider,
#endif   
            TimeSpan lockDuration,
            CancellationToken cancellationToken)
            => TryLockInternal(repository, hashKey, null, lockUntilProperty, timeProvider, lockDuration, cancellationToken);
            
    }
}
