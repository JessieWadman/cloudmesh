using System.Reflection;
using System.Reflection.Emit;

namespace CloudMesh.Serialization
{
    /// <summary>
    /// Helper class
    /// </summary>
    public static class Emitter
    {
        public static readonly AssemblyName AssemblyName = new("Generated_CloudMesh_Types");
        public static readonly AssemblyBuilder AssemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(AssemblyName, AssemblyBuilderAccess.RunAndCollect);
        public static readonly ModuleBuilder ModuleBuilder = AssemblyBuilder.DefineDynamicModule(AssemblyName.Name!);

        /// <summary>
        /// Creates an <see cref="EnumBuilder"/> instance within <paramref name="modBuilder"/>
        /// </summary>
        /// <param name="modBuilder"></param>
        /// <param name="enumName"></param>
        /// <returns></returns>
        public static EnumBuilder GetEnum(ModuleBuilder modBuilder, string enumName)
        {
            EnumBuilder builder = modBuilder.DefineEnum(enumName, TypeAttributes.Public, typeof(int));
            return builder;
        }

        /// <summary>
        /// Create a <see cref="TypeBuilder"/> instance within <paramref name="modBuilder"/>
        /// </summary>
        /// <param name="modBuilder"></param>
        /// <param name="className"></param>
        /// <param name="parent"></param>
        /// <param name="interfaces"></param>
        /// <returns></returns>
        public static TypeBuilder GetType(ModuleBuilder modBuilder, string className, Type? parent = null, Type[]? interfaces = null)
        {
            if (parent == null)
                parent = typeof(object);
            TypeBuilder builder = modBuilder.DefineType(className, TypeAttributes.Public, parent, interfaces);
            return builder;
        }

        public static TypeBuilder GetType(ModuleBuilder modBuilder, string className, params string[] genericparameters)
        {
            TypeBuilder builder = modBuilder.DefineType(className, TypeAttributes.Public);
            GenericTypeParameterBuilder[] genBuilders = builder.DefineGenericParameters(genericparameters);

            foreach (GenericTypeParameterBuilder genBuilder in genBuilders) // We take each generic type T : class, new()
            {
                genBuilder.SetGenericParameterAttributes(GenericParameterAttributes.ReferenceTypeConstraint | GenericParameterAttributes.DefaultConstructorConstraint);
                //genBuilder.SetInterfaceConstraints(interfaces);
            }

            return builder;
        }

        public static MethodBuilder GetMethod(TypeBuilder typeBuilder, string methodName, MethodAttributes attributes)
        {
            MethodBuilder builder = typeBuilder.DefineMethod(
                methodName,
                attributes);
            return builder;
        }

        public static MethodBuilder GetMethod(TypeBuilder typeBuilder, string methodName, MethodAttributes attributes, Type returnType, params Type[] parameterTypes)
        {
            MethodBuilder builder = typeBuilder.DefineMethod(
                methodName,
                attributes,
                CallingConventions.HasThis,
                returnType,
                parameterTypes);
            return builder;
        }

        public static ConstructorBuilder GetConstructor(TypeBuilder typeBuilder, MethodAttributes attributes, params Type[] parameterTypes)
        {
            return typeBuilder.DefineConstructor(attributes, CallingConventions.HasThis, parameterTypes);
        }

        public static MethodBuilder GetMethod(TypeBuilder typeBuilder, string methodName, MethodAttributes attributes, Type returnType, string[] genericParameters, params Type[] parameterTypes)
        {
            MethodBuilder builder = typeBuilder.DefineMethod(
                methodName,
                attributes,
                CallingConventions.HasThis,
                returnType, parameterTypes);

            GenericTypeParameterBuilder[] genBuilders = builder.DefineGenericParameters(genericParameters);

            foreach (GenericTypeParameterBuilder genBuilder in genBuilders) // We take each generic type T : class, new()
            {
                genBuilder.SetGenericParameterAttributes(GenericParameterAttributes.ReferenceTypeConstraint | GenericParameterAttributes.DefaultConstructorConstraint);
                //genBuilder.SetInterfaceConstraints(interfaces);
            }
            return builder;
        }

        public static PropertyInfo DefineProperty(TypeBuilder owner, string name, Type propertyType)
        {
            var fieldBuilder = owner.DefineField(string.Format("<{0}>", name), propertyType, FieldAttributes.Private);
            var getterBuilder = owner.DefineMethod(string.Format("get_{0}", name), MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.SpecialName, propertyType, Type.EmptyTypes);
            ILGenerator getterIl = getterBuilder.GetILGenerator();
            getterIl.Emit(OpCodes.Ldarg_0);
            getterIl.Emit(OpCodes.Ldfld, fieldBuilder);
            getterIl.Emit(OpCodes.Ret);

            var setterBuilder = owner.DefineMethod(string.Format("set_{0}", name), MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.SpecialName, typeof(void), new[] { propertyType });
            ILGenerator setterIl = setterBuilder.GetILGenerator();
            setterIl.Emit(OpCodes.Ldarg_0);
            setterIl.Emit(OpCodes.Ldarg_1);
            setterIl.Emit(OpCodes.Stfld, fieldBuilder);
            setterIl.Emit(OpCodes.Ret);

            var propertyBuilder = owner.DefineProperty(name, PropertyAttributes.None, propertyType, null);
            propertyBuilder.SetGetMethod(getterBuilder);
            propertyBuilder.SetSetMethod(setterBuilder);

            return propertyBuilder;
        }
    }
}