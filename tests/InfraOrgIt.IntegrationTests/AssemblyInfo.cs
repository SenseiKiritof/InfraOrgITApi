using Xunit;

// Un solo container MySQL condiviso, un database per classe. Due classi in
// parallelo riducono la durata senza esaurire Docker sui runner CI.
[assembly: CollectionBehavior(MaxParallelThreads = 2)]
