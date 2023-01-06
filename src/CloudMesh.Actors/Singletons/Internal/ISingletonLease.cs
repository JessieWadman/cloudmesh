using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloudMesh.Singletons.Internal
{
    public interface ISingletonLease
    {
        string SingletonName { get; }
        string UserData { get; }
        ValueTask UpdateUserDataAsync(string userData, TimeSpan leaseDuration);
        ValueTask ReleaseAsync();
    }
}
