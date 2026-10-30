using Xunit;

// Fixture migrations and process-wide telemetry listeners must be serialized.
// The race tests explicitly run independent HTTP requests concurrently within a single test.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
