using Xunit;

// Un solo container MySQL condiviso, uno schema template clonato per classe:
// niente più lock delle migrazioni, quindi le classi possono girare in
// parallelo su database isolati.
[assembly: CollectionBehavior(MaxParallelThreads = 4)]
