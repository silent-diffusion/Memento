namespace Memento.Core.Tests.M3;

/// <summary>Runs alone, so no other test's loopback server can show up in the network spy.</summary>
[CollectionDefinition(nameof(NetworkSilenceGroup), DisableParallelization = true)]
public sealed class NetworkSilenceGroup;
