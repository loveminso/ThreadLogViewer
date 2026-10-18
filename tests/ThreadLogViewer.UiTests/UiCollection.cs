using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ThreadLogViewer.UiTests;

[CollectionDefinition(UiCollection.Name)]
public sealed class UiCollection : ICollectionFixture<PublishedApp>
{
    public const string Name = "ThreadLogViewer UI";
}
