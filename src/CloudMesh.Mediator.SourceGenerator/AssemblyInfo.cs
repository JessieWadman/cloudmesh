using System.Runtime.CompilerServices;

// Expose internal testing seams (e.g. TrackingNames for the cacheability test) to the test project.
[assembly: InternalsVisibleTo("CloudMesh.Mediator.SourceGenerator.Tests")]
