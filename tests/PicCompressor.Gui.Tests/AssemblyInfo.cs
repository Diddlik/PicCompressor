// Der Localizer ist ein prozessweiter Singleton: Sprachwechsel eines Tests wären für parallel
// laufende Tests sichtbar. Die Testklassen laufen daher nacheinander.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
