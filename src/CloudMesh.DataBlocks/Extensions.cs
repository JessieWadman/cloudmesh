using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;

namespace CloudMesh.DataBlocks
{
    internal static class DataBlockFactoryExtensions
    {
        private static long dataBlockNo;

        public static IDataBlock InitializeDataBlock<T>(
            this IDataBlockContainer container,
            Expression<Func<T>> newExpression,
            string? name)
            where T : IDataBlock
        {
            var objectType = typeof(T);

            if (newExpression.Body is UnaryExpression)
                throw new InvalidOperationException();

            var expression = newExpression.Body.AsInstanceOf<NewExpression>();
            if (expression == null)
                throw new ArgumentException("The create function must be a 'new T (args)' expression");

            var args = expression.GetArguments();
            var constructor = expression.Constructor;

            var dataBlock = (T)FormatterServices.GetUninitializedObject(objectType);
            if (dataBlock is IDataBlockInitializer init)
            {
                if (name is null)
                    name = $"{Interlocked.Increment(ref dataBlockNo)}";
                init.Name = name;
                if (container is IDataBlockRef parent)
                    init.Parent = parent;
#pragma warning restore CS8601 // Possible null reference assignment.
            }

            constructor?.Invoke(dataBlock, args);
            return dataBlock;
        }
    }

    internal static class Extensions
    {
        public static T AsInstanceOf<T>(this object self)
        {
            return (T)self;
        }

        /// <summary>
        /// Fetches constructor arguments from a <see cref="NewExpression"/>.
        /// </summary>
        /// <param name="newExpression">The <see cref="NewExpression"/> used typically to create a data block.</param>
        /// <returns>The constructor type and arguments</returns>
        public static object[] GetArguments(this NewExpression newExpression)
        {
            return newExpression.ParseExpressionArgs();
        }
    }

    internal static class ExpressionBasedParser
    {
        private static readonly ConcurrentDictionary<ConstructorInfo, string[]> paramNameDictionary = new ConcurrentDictionary<ConstructorInfo, string[]>();
        private static readonly Type _objectType = typeof(object);

        private static readonly Type _multicastDelegateType =
            typeof(MulticastDelegate);

        public static object[] ParseExpressionArgs(
            this NewExpression newExpr)
        {
            var argProv = newExpr.Arguments;
            var argCount = argProv.Count;
            return ParseCallArgs(argCount, argProv);
        }



        /// <summary>
        /// Parses the arguments for the method call contained in the expression.
        /// </summary>
        private static object[] ParseCallArgs(int argCount,
            ReadOnlyCollection<Expression> argProv)
        {
#pragma warning disable CS8604 // Possible null reference argument.
#pragma warning disable CS8601 // Possible null reference assignment.

            object[] _jobArgs = new object[argCount];
            for (int i = 0; i < argCount; i++)
            {
                var theArg = argProv[i];

                try
                {

                    if (theArg.NodeType == ExpressionType.Constant)
                    {
                        // Happy Case.
                        // If constant, no need for invokes,or anything else

                        _jobArgs[i] = ((ConstantExpression)theArg).Value;
                    }
                    else
                    {
                        if (theArg is MemberExpression _memArg)
                        {
                            if (_memArg.Expression is ConstantExpression ce)
                            {
                                //We don't need .Convert() here because for better or worse
                                //GetValue will box for us.
                                if (_memArg.Member.MemberType == MemberTypes.Field)
                                {
                                    _jobArgs[i] =
                                        ((FieldInfo)_memArg.Member).GetValue(
                                            ce.Value);
                                    theArg = null;
                                }
                                else if (_memArg.Member.MemberType == MemberTypes.Property)
                                {
                                    _jobArgs[i] =
                                        ((PropertyInfo)_memArg.Member).GetValue(
                                            ce.Value);
                                    theArg = null;
                                }
                            }
                        }

                        if (theArg != null)
                        {
                            //If we are dealing with a Valuetype,
                            //we need a convert here.
                            _jobArgs[i] = CompileExprWithConvert(Expression
                                    .Lambda<Func<object>>(
                                        ConvertIfNeeded(theArg)))
                                .Invoke();
                        }
                    }
                }
                catch
                {
                    //Fallback. Do the worst way and compile.
                    try
                    {
                        _jobArgs[i] = Expression.Lambda(
                                Expression.Convert(theArg, _objectType)
                            )
                            .Compile().DynamicInvoke();
                    }

                    catch (Exception ex)
                    {
                        throw new ArgumentException(
                            "Couldn't derive value from Expression! Please use variables whenever possible",
                            ex);
                    }
                }
            }
            return _jobArgs;
#pragma warning restore CS8601 // Possible null reference assignment.
#pragma warning restore CS8604 // Possible null reference argument.
        }


        private static Expression ConvertIfNeeded(Expression toConv)
        {
            Type retType;
            if (toConv.NodeType == ExpressionType.Lambda)
            {

#pragma warning disable CS8604 // Possible null reference argument.
                retType = TraverseForType(toConv.Type.GetGenericArguments()
                    .LastOrDefault());
#pragma warning restore CS8604 // Possible null reference argument.
            }
            else
            {
                retType = toConv.Type;
            }

            if (retType?.BaseType == _objectType)
            {
                return toConv;
            }
            else
            {
                return Expression.Convert(toConv, _objectType);
            }
        }

        private static Type TraverseForType(Type toConv)
        {
#pragma warning disable CS8604 // Possible null reference argument.
            if (toConv == null)
            {
#pragma warning disable CS8603 // Possible null reference return.
                return null;
#pragma warning restore CS8603 // Possible null reference return.
            }
            else if (toConv == _multicastDelegateType)
            {
                //I don't think this should happen in sane usage, but let's cover it.
                return TraverseForType(toConv.GetGenericArguments().LastOrDefault());
            }
            else
            {
                return toConv;
            }
#pragma warning restore CS8604 // Possible null reference argument.
        }

        private static T CompileExprWithConvert<T>(Expression<T> lambda) where T : class
        {
            return lambda.Compile();
        }
    }
}
