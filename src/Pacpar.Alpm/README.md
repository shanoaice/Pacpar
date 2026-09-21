# Pacpar.Alpm

## libalpm compatibility

Pacpar.Alpm always targets the latest version of libalpm. It may also support previous versions if the API was not changed.

## Log messages

libalpm hands `alpm_cb_log` a `(fmt, va_list)` pair rather than expanded arguments, so the message
has to be formatted by the callee. `LogMessageFormatter` passes the `va_list` through as an opaque
pointer to libc's `vasprintf`; the library ships no native artifact of its own.
