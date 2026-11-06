#if NETFRAMEWORK
namespace System.Runtime.CompilerServices
{
    /// <summary>Lets [ModuleInitializer] compile on .NET Framework (the C# compiler recognizes it by name).</summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute { }

    internal static class IsExternalInit { }
}
#endif
