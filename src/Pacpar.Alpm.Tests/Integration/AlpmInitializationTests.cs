using Pacpar.Alpm.Tests.Fixtures;

namespace Pacpar.Alpm.Tests.Integration;

[Collection(AlpmEnvironmentCollection.Name)]
public sealed class AlpmInitializationTests(AlpmEnvironmentFixture fixture)
{
  [Fact]
  [Trait("Category", "Integration")]
  public void Constructor_InitializesHandle_WithIsolatedFilesystem()
  {
    using var alpm = fixture.CreateAlpm();

    Assert.NotEqual(IntPtr.Zero, alpm.Handle);
    Assert.Equal(Pacpar.Alpm.Bindings._alpm_errno_t.ALPM_ERR_OK, alpm.Errno);
    Assert.Null(alpm.GetCurrentError());
  }

  [Fact]
  [Trait("Category", "Integration")]
  public void GetLocalDatabase_ReturnsLocalDatabase()
  {
    using var alpm = fixture.CreateAlpm();

    var database = alpm.GetLocalDatabase();

    Assert.Equal("local", database.Name);
  }

  [Fact]
  [Trait("Category", "Integration")]
  public void GetSyncDatabases_ReturnsEmptyList_WhenNoSyncDatabasesRegistered()
  {
    using var alpm = fixture.CreateAlpm();

    // Borrowed view: the list is owned by the handle, so there is nothing to dispose here.
    var databases = alpm.GetSyncDatabases();

    Assert.Empty(databases);
  }

  [Fact]
  [Trait("Category", "Integration")]
  public void Dispose_InvalidatesManagedAccessors()
  {
    var alpm = fixture.CreateAlpm();
    alpm.Dispose();

    Assert.Throws<ObjectDisposedException>(() => _ = alpm.Handle);
    Assert.Throws<ObjectDisposedException>(() => _ = alpm.Errno);
    Assert.Throws<ObjectDisposedException>(() => alpm.GetCurrentError());
  }
}
