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
  [InlineData(_alpm_errno_t.ALPM_ERR_BADPERMS, typeof(UnauthorizedAccessException))]
  [InlineData(_alpm_errno_t.ALPM_ERR_SYSTEM, typeof(SystemException))]
  [InlineData(_alpm_errno_t.ALPM_ERR_DB_NOT_FOUND, typeof(FileNotFoundException))]
  [InlineData(_alpm_errno_t.ALPM_ERR_DB_INVALID, typeof(InvalidOperationException))]
  [InlineData(_alpm_errno_t.ALPM_ERR_MISSING_CAPABILITY_SIGNATURES, typeof(NotSupportedException))]
  public void GetException_ReturnsExpectedExceptionType(_alpm_errno_t errno, Type expectedType)
  {
    var exception = ErrorHandler.GetException(errno);

    Assert.NotNull(exception);
    Assert.IsType(expectedType, exception);
  }

  [Theory]
  [InlineData(_alpm_errno_t.ALPM_ERR_NOT_A_FILE, "file")]
  [InlineData(_alpm_errno_t.ALPM_ERR_NOT_A_DIR, "directory")]
  [InlineData(_alpm_errno_t.ALPM_ERR_WRONG_ARGS, "arguments")]
  public void GetException_ReturnsExpectedParameterName_ForArgumentErrors(_alpm_errno_t errno, string parameterName)
  {
    var exception = Assert.IsType<ArgumentException>(ErrorHandler.GetException(errno));

    Assert.Equal(parameterName, exception.ParamName);
  }

  [Fact]
  public void GetException_MapsRetrievePrepare_AfterTheLibalpmUpdate()
  {
    // Regression: ALPM_ERR_RETRIEVE_PREPARE ("Download setup failed") arrived with libalpm 16 and
    // the hand-written switch was not updated, so this call used to return null.
    var exception = ErrorHandler.GetException(_alpm_errno_t.ALPM_ERR_RETRIEVE_PREPARE);

    Assert.NotNull(exception);
    Assert.IsNotType<AlpmException>(exception);
  }

  /// <summary>
  /// Every errno the bindings know about must have an explicit mapping. A libalpm update that adds an
  /// errno fails this test instead of silently degrading to the fallback at runtime.
  /// </summary>
  [Fact]
  public void GetException_MapsEveryKnownErrno_ToASpecificException()
  {
    var unmapped = Enum.GetValues<_alpm_errno_t>()
      .Where(errno => errno != _alpm_errno_t.ALPM_ERR_OK)
      .Where(errno => ErrorHandler.GetException(errno) is null or AlpmException)
      .ToArray();

    Assert.Empty(unmapped);
  }

  [Fact]
  public void GetException_ReturnsFallback_ForAnErrnoTheBindingsDoNotKnow()
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
