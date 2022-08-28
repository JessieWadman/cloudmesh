using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloudMesh.Actors.Observability
{
    public class ConsoleSpan : IObservableSpan
    {
        private readonly Dictionary<string, string> annotations = new();

        public ConsoleSpan(string rootContextId, string parentContextId)
        {
            RootContextId = rootContextId ?? Guid.NewGuid().ToString();
            ParentContextId = parentContextId;
            ContextId = Guid.NewGuid().ToString();
        }

        public string RootContextId { get; }
        public string ParentContextId { get; }
        public string ContextId { get; }

        public void Annotate(string key, string value)
        {
            annotations[key] = value;
        }

        public void Complete()
        {
            throw new NotImplementedException();
        }

        public void Fail(int errorCode, string error)
        {
            throw new NotImplementedException();
        }

        public void Trace(string message)
        {
            throw new NotImplementedException();
        }
    }

    public class ConsoleObserver : IObserver //, IObservableSpan
    {
        private readonly Dictionary<string, string> annotations = new();

        public ConsoleObserver(string rootContextId, string spanName)
        {
            RootContextId = rootContextId ?? Guid.NewGuid().ToString();
            CorrelationId = Guid.NewGuid().ToString();
            this.StartedUtc = DateTime.UtcNow;
        }

        public string RootContextId { get; }
        public string CorrelationId { get; }
        public DateTime StartedUtc { get; }

        public void Annotate(string key, string value)
        {
            annotations[key] = value;
        }

        public IObservableSpan BeginSpan(string spanName)
        {
            throw new NotImplementedException();
            // return new ConsoleObserver(this.RootContextId, spanName);
        }

        public void Complete()
        {
            throw new NotImplementedException();
        }

        public void Fail(int errorCode, string error)
        {
            throw new NotImplementedException();
        }

        public void Trace(string message)
        {
            throw new NotImplementedException();
        }
    }
}
