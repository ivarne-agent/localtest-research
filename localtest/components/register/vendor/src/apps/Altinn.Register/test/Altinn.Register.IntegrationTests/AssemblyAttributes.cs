using Altinn.Register.TestUtils;

[assembly: CollectionBehavior(typeof(AltinnTestCollectionFactory), DisableTestParallelization = false, MaxParallelThreads = AltinnTestCollectionFactory.MaxConcurrency)]
