using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm.Tests.Unit;

public sealed class ErrorHandlerTests
{
  [Fact]
  public void GetException_ReturnsNull_ForOk()
  {
    var exception = ErrorHandler.GetException(_alpm_errno_t.ALPM_ERR_OK);

    Assert.Null(exception);
  }

  [Theory]
  [InlineData(_alpm_errno_t.ALPM_ERR_MEMORY, typeof(OutOfMemoryException))]
  [InlineData(_alpm_errno_t.ALPM_ERR_BADPERMS, typeof(AlpmException))]
  [InlineData(_alpm_errno_t.ALPM_ERR_SYSTEM, typeof(AlpmException))]
  [InlineData(_alpm_errno_t.ALPM_ERR_DB_NOT_FOUND, typeof(AlpmDatabaseException))]
  [InlineData(_alpm_errno_t.ALPM_ERR_DB_INVALID, typeof(AlpmDatabaseException))]
  [InlineData(_alpm_errno_t.ALPM_ERR_PKG_NOT_FOUND, typeof(AlpmPackageException))]
  [InlineData(_alpm_errno_t.ALPM_ERR_TRANS_NOT_NULL, typeof(AlpmTransactionException))]
  [InlineData(_alpm_errno_t.ALPM_ERR_SIG_MISSING, typeof(AlpmSignatureException))]
  [InlineData(_alpm_errno_t.ALPM_ERR_MISSING_CAPABILITY_SIGNATURES, typeof(AlpmSignatureException))]
  [InlineData(_alpm_errno_t.ALPM_ERR_RETRIEVE, typeof(AlpmRetrieveException))]
  public void GetException_ReturnsExpectedExceptionType(_alpm_errno_t errno, Type expectedType)
  {
    var exception = ErrorHandler.GetException(errno);

    Assert.NotNull(exception);
    Assert.IsType(expectedType, exception);
  }

  [Theory]
  [InlineData(_alpm_errno_t.ALPM_ERR_NOT_A_FILE)]
  [InlineData(_alpm_errno_t.ALPM_ERR_NOT_A_DIR)]
  [InlineData(_alpm_errno_t.ALPM_ERR_WRONG_ARGS)]
  public void GetException_DoesNotDisguiseNativeErrorsAsArgumentErrors(_alpm_errno_t errno)
  {
    // Report §E-6: a native condition is not caller misuse. Reporting it as an ArgumentException
    // made it indistinguishable from real parameter validation, which does carry a ParamName.
    var exception = Assert.IsType<AlpmException>(ErrorHandler.GetException(errno));

    Assert.Equal(errno, exception.Errno);
  }

  [Fact]
  public void AlpmException_CarriesErrnoStrErrorAndContext()
  {
    var exception = new AlpmPackageException(
      _alpm_errno_t.ALPM_ERR_PKG_NOT_FOUND,
      context: "Failed to add package: foo");

    Assert.Equal(_alpm_errno_t.ALPM_ERR_PKG_NOT_FOUND, exception.Errno);
    Assert.False(string.IsNullOrEmpty(exception.StrError));
    Assert.Contains("Failed to add package: foo", exception.Message);
    Assert.Contains("ALPM_ERR_PKG_NOT_FOUND", exception.Message);
  }

  [Fact]
  public void GetException_MapsRetrievePrepare_AfterTheLibalpmUpdate()
  {
    // Regression: ALPM_ERR_RETRIEVE_PREPARE ("Download setup failed") arrived with libalpm 16 and
    // the hand-written switch was not updated, so this call used to return null.
    var exception = Assert.IsType<AlpmRetrieveException>(
      ErrorHandler.GetException(_alpm_errno_t.ALPM_ERR_RETRIEVE_PREPARE));

    Assert.Equal(_alpm_errno_t.ALPM_ERR_RETRIEVE_PREPARE, exception.Errno);
  }

  /// <summary>
  /// Every errno the bindings know about must be classified. A libalpm update that adds an errno
  /// fails this test instead of silently degrading to the generic <see cref="AlpmException"/>.
  /// </summary>
  [Fact]
  public void Categorize_ClassifiesEveryKnownErrno()
  {
    var unknown = Enum.GetValues<_alpm_errno_t>()
      .Where(errno => errno != _alpm_errno_t.ALPM_ERR_OK)
      .Where(errno => ErrorHandler.Categorize(errno) == AlpmErrorCategory.Unknown)
      .ToArray();

    Assert.Empty(unknown);
  }

  [Fact]
  public void GetException_ReturnsGenericAlpmException_ForAnErrnoTheBindingsDoNotKnow()
  {
    var errno = (_alpm_errno_t)ushort.MaxValue;

    var exception = Assert.IsType<AlpmException>(ErrorHandler.GetException(errno));

    Assert.Equal(errno, exception.Errno);
    Assert.Contains(((int)errno).ToString(), exception.Message);
    Assert.False(string.IsNullOrEmpty(exception.StrError));
  }

  [Fact]
  public void ToException_Throws_ForOk()
    => Assert.Throws<ArgumentOutOfRangeException>(() => ErrorHandler.ToException(_alpm_errno_t.ALPM_ERR_OK));

  [Fact]
  public void ToException_NeverReturnsNull_ForAnyKnownErrno()
  {
    foreach (var errno in Enum.GetValues<_alpm_errno_t>().Where(e => e != _alpm_errno_t.ALPM_ERR_OK))
    {
      Assert.NotNull(ErrorHandler.ToException(errno));
    }
  }
}
