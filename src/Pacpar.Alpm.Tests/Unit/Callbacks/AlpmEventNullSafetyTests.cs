using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm.Tests.Unit.Callbacks;

public sealed unsafe class AlpmEventNullSafetyTests
{
  [Fact]
  public void PacnewCreated_WithNullNativePackages_YieldsNullViews()
  {
    var native = stackalloc _alpm_event_t[1];
    native->type_ = _alpm_event_type_t.ALPM_EVENT_PACNEW_CREATED;
    native->pacnew_created.oldpkg = null;
    native->pacnew_created.newpkg = null;
    native->pacnew_created.file = null;
    native->pacnew_created.from_noupgrade = 1;

    var ev = Assert.IsType<AlpmEvent.PacnewCreated>(AlpmEvent.FromUnion(native, null));

    Assert.True(ev.FromNoUpgrade);
    Assert.Null(ev.OldPackage);
    Assert.Null(ev.NewPackage);
    Assert.Equal("", ev.File);
  }

  [Fact]
  public void PacsaveCreated_WithNullNativePackage_YieldsNullView()
  {
    var native = stackalloc _alpm_event_t[1];
    native->type_ = _alpm_event_type_t.ALPM_EVENT_PACSAVE_CREATED;
    native->pacsave_created.oldpkg = null;
    native->pacsave_created.file = null;

    var ev = Assert.IsType<AlpmEvent.PacsaveCreated>(AlpmEvent.FromUnion(native, null));

    Assert.Null(ev.OldPackage);
    Assert.Equal("", ev.File);
  }

  [Fact]
  public void PackageOperationStart_WithNullNativePackages_YieldsNullViews()
  {
    var native = stackalloc _alpm_event_t[1];
    native->type_ = _alpm_event_type_t.ALPM_EVENT_PACKAGE_OPERATION_START;
    native->package_operation.newpkg = null;
    native->package_operation.oldpkg = null;
    native->package_operation.operation = _alpm_package_operation_t.ALPM_PACKAGE_REMOVE;

    var ev = Assert.IsType<AlpmEvent.PackageOperationStart>(AlpmEvent.FromUnion(native, null));

    Assert.Null(ev.NewPackage);
    Assert.Null(ev.OldPackage);
    Assert.Equal(PackageOperation.Remove, ev.Operation);
  }

  [Fact]
  public void PackageOperationDone_WithNullNativePackages_YieldsNullViews()
  {
    var native = stackalloc _alpm_event_t[1];
    native->type_ = _alpm_event_type_t.ALPM_EVENT_PACKAGE_OPERATION_DONE;
    native->package_operation.newpkg = null;
    native->package_operation.oldpkg = null;
    native->package_operation.operation = _alpm_package_operation_t.ALPM_PACKAGE_INSTALL;

    var ev = Assert.IsType<AlpmEvent.PackageOperationDone>(AlpmEvent.FromUnion(native, null));

    Assert.Null(ev.NewPackage);
    Assert.Null(ev.OldPackage);
    Assert.Equal(PackageOperation.Install, ev.Operation);
  }
}
