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
  [InlineData((int)_alpm_errno_t.ALPM_ERR_MEMORY, typeof(OutOfMemoryException))]
  [InlineData((int)_alpm_errno_t.ALPM_ERR_BADPERMS, typeof(AlpmException))]
  [InlineData((int)_alpm_errno_t.ALPM_ERR_SYSTEM, typeof(AlpmException))]
  [InlineData((int)_alpm_errno_t.ALPM_ERR_DB_NOT_FOUND, typeof(AlpmDatabaseException))]
  [InlineData((int)_alpm_errno_t.ALPM_ERR_DB_INVALID, typeof(AlpmDatabaseException))]
  [InlineData((int)_alpm_errno_t.ALPM_ERR_PKG_NOT_FOUND, typeof(AlpmPackageException))]
  [InlineData((int)_alpm_errno_t.ALPM_ERR_TRANS_NOT_NULL, typeof(AlpmTransactionException))]
  [InlineData((int)_alpm_errno_t.ALPM_ERR_SIG_MISSING, typeof(AlpmSignatureException))]
  [InlineData((int)_alpm_errno_t.ALPM_ERR_MISSING_CAPABILITY_SIGNATURES, typeof(AlpmSignatureException))]
  [InlineData((int)_alpm_errno_t.ALPM_ERR_RETRIEVE, typeof(AlpmRetrieveException))]
  [InlineData((int)_alpm_errno_t.ALPM_ERR_RETRIEVE_PREPARE, typeof(AlpmRetrieveException))]
  public void GetException_ReturnsExpectedExceptionType(int errno, Type expectedType)
  {
    var exception = ErrorHandler.GetException(errno);

    Assert.NotNull(exception);
    Assert.IsType(expectedType, exception);
  }

  [Theory]
  [InlineData((int)_alpm_errno_t.ALPM_ERR_NOT_A_FILE)]
  [InlineData((int)_alpm_errno_t.ALPM_ERR_NOT_A_DIR)]
  [InlineData((int)_alpm_errno_t.ALPM_ERR_WRONG_ARGS)]
  public void GetException_DoesNotDisguiseNativeErrorsAsArgumentErrors(int errno)
  {
    // A native condition is not caller misuse. Reporting it as an ArgumentException
    // made it indistinguishable from real parameter validation, which does carry a ParamName.
    var exception = Assert.IsType<AlpmException>(ErrorHandler.GetException(errno));

    Assert.Equal(errno, exception.Errno);
  }

  [Fact]
  public void AlpmException_CarriesErrnoStrErrorAndContext()
  {
    var exception = new AlpmPackageException(
      (int)_alpm_errno_t.ALPM_ERR_PKG_NOT_FOUND,
      context: "Failed to add package: foo");

    Assert.Equal((int)_alpm_errno_t.ALPM_ERR_PKG_NOT_FOUND, exception.Errno);
    Assert.False(string.IsNullOrEmpty(exception.StrError));
    Assert.Contains("Failed to add package: foo", exception.Message);
    Assert.Contains("ALPM_ERR_PKG_NOT_FOUND", exception.Message);
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
  public void CodeOf_KnowsEveryKnownErrno()
  {
    var unknown = Enum.GetValues<_alpm_errno_t>()
      .Where(errno => errno != _alpm_errno_t.ALPM_ERR_OK && errno != _alpm_errno_t.ALPM_ERR_MEMORY)
      .Where(errno => ErrorHandler.CodeOf((int)errno) == AlpmFailureCode.UnknownNative)
      .ToArray();

    Assert.Empty(unknown);
  }

  [Fact]
  public void ToException_FromAlpmFailure_YieldsCategorizedExceptionType()
  {
    foreach (var errno in Enum.GetValues<_alpm_errno_t>())
    {
      if (errno == _alpm_errno_t.ALPM_ERR_OK || errno == _alpm_errno_t.ALPM_ERR_MEMORY) continue;

      var failure = AlpmFailure.Of((int)errno, "test");
      var ex = failure.ToException();

      var expectedType = ErrorHandler.Categorize(errno) switch
      {
        AlpmErrorCategory.Database => typeof(AlpmDatabaseException),
        AlpmErrorCategory.Package => typeof(AlpmPackageException),
        AlpmErrorCategory.Transaction => typeof(AlpmTransactionException),
        AlpmErrorCategory.Signature => typeof(AlpmSignatureException),
        AlpmErrorCategory.Retrieve => typeof(AlpmRetrieveException),
        _ => typeof(AlpmException)
      };

      Assert.IsType(expectedType, ex);
    }
  }

  [Fact]
  public void GetException_ReturnsGenericAlpmException_ForAnErrnoTheBindingsDoNotKnow()
  {
    var errno = (_alpm_errno_t)ushort.MaxValue;

    var exception = Assert.IsType<AlpmException>(ErrorHandler.GetException(errno));

    Assert.Equal((int)errno, exception.Errno);
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

  /// <summary>
  /// ADR 0007: the four <c>ALPM_ERR_PKG_INVALID*</c> errnos carry a payload - a list of package
  /// names - when a transaction call fails, and <see cref="AlpmTransactionException.TakeFailure"/>
  /// reports them as its <c>InvalidPackage*</c> cases. One errno has one public exception type, so
  /// the errno factory has to agree with that: it cannot answer
  /// <see cref="AlpmPackageException"/> for the same errno.
  /// </summary>
  /// <remarks>
  /// Both entry points are pinned because only the transaction side was covered before, and the
  /// classification test asserts nothing stronger than "not unknown". The generic path has no list
  /// to carry, so it answers with the base state of the same type rather than with a different type.
  /// </remarks>
  [Theory]
  [InlineData((int)_alpm_errno_t.ALPM_ERR_PKG_INVALID)]
  [InlineData((int)_alpm_errno_t.ALPM_ERR_PKG_INVALID_CHECKSUM)]
  [InlineData((int)_alpm_errno_t.ALPM_ERR_PKG_INVALID_SIG)]
  [InlineData((int)_alpm_errno_t.ALPM_ERR_PKG_INVALID_ARCH)]
  public unsafe void PayloadErrnos_AreReportedAsTransactionErrors_FromBothEntryPoints(int errno)
  {
    Assert.Equal(AlpmErrorCategory.Transaction, ErrorHandler.Categorize((_alpm_errno_t)errno));

    var generic = Assert.IsType<AlpmTransactionException>(ErrorHandler.GetException(errno));
    var transaction = Assert.IsAssignableFrom<AlpmTransactionException>(
      AlpmFailure.Take(errno, null, "Failed to prepare transaction").ToException());
    Assert.IsAssignableFrom<AlpmTransactionException>(transaction);
    Assert.IsNotAssignableFrom<AlpmPackageException>(transaction);

    Assert.Equal(errno, generic.Errno);
    Assert.Equal(errno, transaction.Errno);

    // The transaction side is the payload case for this errno - not the base state - and a call
    // that dumped no list yields an empty payload, not a missing one.
    var packages = transaction switch
    {
      AlpmTransactionException.InvalidPackage invalid => invalid.Packages,
      AlpmTransactionException.InvalidPackageChecksum checksum => checksum.Packages,
      AlpmTransactionException.InvalidPackageSignature signature => signature.Packages,
      AlpmTransactionException.InvalidPackageArchitecture arch => arch.Packages,
      _ => null
    };

    Assert.NotNull(packages);
    Assert.Empty(packages);
  }
}
