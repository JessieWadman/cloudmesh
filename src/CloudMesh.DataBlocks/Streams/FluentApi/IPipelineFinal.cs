namespace CloudMesh.DataBlocks.Streams.FluentApi;

/// <summary>A terminated pipeline (a sink has been chosen), ready to build into a running <see cref="IPipeline{TOriginalInput}"/>.</summary>
/// <typeparam name="TOriginalInput">The pipeline's original input type.</typeparam>
public interface IPipelineFinal<TOriginalInput>
{
    /// <summary>
    /// Materializes the pipeline: wires the stages back-to-front, starts every block, and (for a self-pumping
    /// source) begins pumping. The returned pipeline is live immediately.
    /// </summary>
    /// <returns>The running pipeline.</returns>
    IPipeline<TOriginalInput> Build();
}
