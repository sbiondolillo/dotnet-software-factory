using Xunit;

// xUnit v3 runs test classes in parallel by default; a concurrently running unit-test
// sleep would pollute the command-runner scenario's sleep-PID snapshot, so the whole
// suite runs sequentially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
