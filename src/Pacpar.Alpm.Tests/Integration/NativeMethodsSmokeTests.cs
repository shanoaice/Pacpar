using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Integration;

[Collection(AlpmEnvironmentCollection.Name)]
public sealed class NativeMethodsSmokeTests(AlpmEnvironmentFixture fixture)
{
  [Fact]
  [Trait("Category", "Integration")]
  public void LibalpmVersion_IsAvailable()
  {
    Assert.Matches("^[0-9]+\\.[0-9]+\\.[0-9]+", fixture.LibalpmVersion);
  }
}
