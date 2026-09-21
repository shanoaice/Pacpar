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
}
