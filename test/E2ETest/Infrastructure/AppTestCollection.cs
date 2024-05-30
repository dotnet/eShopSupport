
namespace E2ETest.Infrastructure;

[CollectionDefinition(Name)]
public class AppTestCollection : ICollectionFixture<AppHostFixture>
{
    public const string Name = "apptest collection";
}
