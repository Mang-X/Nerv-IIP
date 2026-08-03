using Xunit;

[assembly: TestCaseOrderer(
    "Nerv.IIP.Testing.Xunit.SeededTestCaseOrderer",
    "Nerv.IIP.Testing.Xunit")]
[assembly: TestCollectionOrderer(
    "Nerv.IIP.Testing.Xunit.SeededTestCollectionOrderer",
    "Nerv.IIP.Testing.Xunit")]
