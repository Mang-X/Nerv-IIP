// Shared by every test assembly that opts into seeded ordering. Linked in through
// SeededTestOrdering.targets so the four target assemblies keep one copy, not four.
using Xunit;

[assembly: TestCaseOrderer(
    "Nerv.IIP.Testing.Xunit.SeededTestCaseOrderer",
    "Nerv.IIP.Testing.Xunit")]
[assembly: TestCollectionOrderer(
    "Nerv.IIP.Testing.Xunit.SeededTestCollectionOrderer",
    "Nerv.IIP.Testing.Xunit")]
