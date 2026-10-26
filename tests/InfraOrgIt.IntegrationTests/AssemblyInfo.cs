using Xunit;

// Un container MySQL per classe: eseguirle in parallelo esaurisce Docker e rende i test instabili.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
