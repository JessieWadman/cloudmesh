using Proto;

namespace CartServices
{
    public static class GrainHelpers
    {
        public static bool StopOnReceiveTimeout(this IContext context)
        {
            if (context.Message is ReceiveTimeout)
            {
                context.Poison(context.Self);
                return true;
            }
            return false;
        }
    }
}
