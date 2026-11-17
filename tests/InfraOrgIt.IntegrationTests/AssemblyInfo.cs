using Xunit;

// Un solo container MySQL condiviso, un database per classe. Le classi girano
// in sequenza: le migrazioni EF si serializzano sul lock globale
// __EFMigrationsLock e due WebApplicationFactory in parallelo possono bloccare
// il testhost. Misurato: ~4m40s in sequenza, 6-8 min con due classi.
[assembly: CollectionBehavior(MaxParallelThreads = 1)]
