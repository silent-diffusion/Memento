namespace Memento.AI.Tests.Cloud;

/// <summary>Runs alone, so no other test's requests can show up in the network spy.</summary>
[CollectionDefinition(nameof(CloudDestinationGroup), DisableParallelization = true)]
public sealed class CloudDestinationGroup;
