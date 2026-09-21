namespace Pacpar.Alpm.Tests.Fixtures;

[CollectionDefinition(Name)]
public sealed class AlpmEnvironmentCollection : ICollectionFixture<AlpmEnvironmentFixture>
{
  public const string Name = "ALPM environment";
}
