using Xunit;

// Un solo container MySQL condiviso, un database per classe. Le classi girano
// in sequenza: due WebApplicationFactory avviate insieme possono perdere
// l'intercettazione di app.Run e bloccare il testhost.
[assembly: CollectionBehavior(MaxParallelThreads = 1)]
