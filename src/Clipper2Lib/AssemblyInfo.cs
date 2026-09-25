using System.Runtime.CompilerServices;

// nb: the engine's intersection sorter and node type are internal, and the test
// suite checks them directly (ordering, multiset, exact agreement with the
// reference relation on distinct keys)
[assembly: InternalsVisibleTo("Clipper2.Tests")]
