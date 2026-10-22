using Xunit;

// Tests override the process-wide GOTO_HOME env var to isolate the data
// directory per test; parallel test classes would race on it otherwise.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
