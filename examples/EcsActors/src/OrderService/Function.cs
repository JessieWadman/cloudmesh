using CloudMesh.Hosting.Lambda;
using OrderService.Services;

#if (!DEBUG)
// Note: Change output type to EXE
await LambdaServiceHost<Orders>.Create()
    .Build()
    .RunAsync();
#endif

#if (DEBUG)
namespace OrderService
{
    // Lambda test tool cannot handle top-level statements. Yet.
    // Note: Change output type from EXE to DLL
    public class DebugHost : LambdaHost<Orders> { }
}
#endif