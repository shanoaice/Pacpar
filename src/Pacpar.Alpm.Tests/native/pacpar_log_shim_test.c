/*
 * Test-only harness for the va_list forwarding contract.
 *
 * C# cannot materialise a va_list, so the only way to exercise the real path is from C: this
 * file plays the role of libalpm firing alpm_cb_log, i.e. it builds a genuine va_list and hands
 * it to a callback declared as taking va_list. The managed thunk under test receives that
 * argument as an opaque void* and must forward it to pacpar_shim_vformat unchanged.
 *
 * It is deliberately standalone (no link dependency on the shim) so the same source can be
 * compiled for any architecture: running the test suite on Arch Linux ARM re-checks the ABI
 * claim on real AArch64 hardware instead of relying on the ABI documents.
 *
 * The exported entry point is fixed-arity because C# cannot call a C variadic function; the
 * variadic call site lives in the private helper below.
 */
#include <stdarg.h>

/* Compiled with the same -fvisibility=hidden flags as the shim it drives. */
#define PACPAR_SHIM_TEST_API __attribute__((visibility("default")))

typedef void (*pacpar_shim_test_logcb)(void *ctx, int level, const char *fmt, va_list ap);

static void pacpar_shim_test_emit(pacpar_shim_test_logcb cb, void *ctx, int level,
                                  const char *fmt, ...)
{
  va_list ap;
  va_start(ap, fmt);
  cb(ctx, level, fmt, ap);
  va_end(ap);
}

/* `text` covers the general-purpose argument registers, `fraction` the floating-point ones. */
PACPAR_SHIM_TEST_API void pacpar_shim_test_invoke(pacpar_shim_test_logcb cb, void *ctx, int level,
                                                  const char *fmt, const char *text, int number,
                                                  double fraction)
{
  pacpar_shim_test_emit(cb, ctx, level, fmt, text, number, fraction);
}
