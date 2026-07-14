namespace CloudMesh.Utils
{
    /// <summary>Represents an operation or resource that can be cancelled on demand.</summary>
    public interface ICancelable
    {
        /// <summary>Cancels the operation.</summary>
        void Cancel();
    }
}
