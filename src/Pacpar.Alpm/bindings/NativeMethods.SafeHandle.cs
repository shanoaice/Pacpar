// Lease-typed mirror of the native bindings in NativeMethods.libalpm.g.cs.
//
// Every libalpm entry point that takes the library context (`alpm_handle_t* handle`) also has an
// overload here that takes the owning SafeAlpmHandle instead. Handing the SafeHandle to the
// runtime's SafeHandleMarshaller makes the marshaller take a refcount on the handle for the whole
// duration of the call and release it afterwards, so the handle cannot be released - and
// alpm_release cannot run - while a native call is in flight. That is strictly stronger than
// anchoring the owner with GC.KeepAlive at the call site, and it is why the wrapper no longer
// needs keep-alives around its libalpm calls.
//
// This file is hand-maintained and guarded by SafeBindingPairingTests, which pairs every overload
// below with the [DllImport] it mirrors (by EntryPoint) and fails on any arity, parameter-type or
// return-type drift introduced by a libalpm upgrade plus a csbindgen regeneration.
//
// alpm_release is deliberately absent: SafeAlpmHandle.ReleaseHandle() calls it, and a SafeHandle
// must not be marshaled from inside its own release path.
using System.Runtime.InteropServices;

namespace Pacpar.Alpm.Bindings;

public static unsafe partial class NativeMethods
{
  [LibraryImport(__DllName, EntryPoint = "alpm_errno")]
  internal static partial _alpm_errno_t alpm_errno(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_get_localdb")]
  internal static partial _alpm_db_t* alpm_get_localdb(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_get_syncdbs")]
  internal static partial _alpm_list_t* alpm_get_syncdbs(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_register_syncdb")]
  internal static partial _alpm_db_t* alpm_register_syncdb(SafeAlpmHandle handle, byte* treename, int level);

  [LibraryImport(__DllName, EntryPoint = "alpm_unregister_all_syncdbs")]
  internal static partial int alpm_unregister_all_syncdbs(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_get_logcb")]
  internal static partial delegate* unmanaged[Cdecl]<void*, _alpm_loglevel_t, byte*, __va_list_tag*, void> alpm_option_get_logcb(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_get_dlcb")]
  internal static partial delegate* unmanaged[Cdecl]<void*, byte*, _alpm_download_event_type_t, void*, void> alpm_option_get_dlcb(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_set_dlcb")]
  internal static partial int alpm_option_set_dlcb(SafeAlpmHandle handle, delegate* unmanaged[Cdecl]<void*, byte*, _alpm_download_event_type_t, void*, void> cb, void* ctx);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_get_fetchcb")]
  internal static partial delegate* unmanaged[Cdecl]<void*, byte*, byte*, int, int> alpm_option_get_fetchcb(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_set_fetchcb")]
  internal static partial int alpm_option_set_fetchcb(SafeAlpmHandle handle, delegate* unmanaged[Cdecl]<void*, byte*, byte*, int, int> cb, void* ctx);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_get_eventcb")]
  internal static partial delegate* unmanaged[Cdecl]<void*, _alpm_event_t*, void> alpm_option_get_eventcb(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_set_eventcb")]
  internal static partial int alpm_option_set_eventcb(SafeAlpmHandle handle, delegate* unmanaged[Cdecl]<void*, _alpm_event_t*, void> cb, void* ctx);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_get_questioncb")]
  internal static partial delegate* unmanaged[Cdecl]<void*, _alpm_question_t*, void> alpm_option_get_questioncb(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_set_questioncb")]
  internal static partial int alpm_option_set_questioncb(SafeAlpmHandle handle, delegate* unmanaged[Cdecl]<void*, _alpm_question_t*, void> cb, void* ctx);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_get_progresscb")]
  internal static partial delegate* unmanaged[Cdecl]<void*, _alpm_progress_t, byte*, int, nuint, nuint, void> alpm_option_get_progresscb(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_set_progresscb")]
  internal static partial int alpm_option_set_progresscb(SafeAlpmHandle handle, delegate* unmanaged[Cdecl]<void*, _alpm_progress_t, byte*, int, nuint, nuint, void> cb, void* ctx);

  // Deliberately keeps the va_list argument of cb opaque (void* instead of the generated
  // __va_list_tag*): Callback.LogAgent forwards it unchanged to the native vasprintf shim, and a
  // regenerated binding must never re-materialise a typed va_list in the thunk signature. The
  // deviation is whitelisted in SafeBindingPairingTests.IntentionalParameterDeviations.
  [LibraryImport(__DllName, EntryPoint = "alpm_option_set_logcb")]
  internal static partial int alpm_option_set_logcb(SafeAlpmHandle handle, delegate* unmanaged[Cdecl]<void*, _alpm_loglevel_t, byte*, void*, void> cb, void* ctx);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_get_root")]
  internal static partial byte* alpm_option_get_root(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_get_dbpath")]
  internal static partial byte* alpm_option_get_dbpath(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_get_lockfile")]
  internal static partial byte* alpm_option_get_lockfile(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_get_cachedirs")]
  internal static partial _alpm_list_t* alpm_option_get_cachedirs(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_add_cachedir")]
  internal static partial int alpm_option_add_cachedir(SafeAlpmHandle handle, byte* cachedir);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_remove_cachedir")]
  internal static partial int alpm_option_remove_cachedir(SafeAlpmHandle handle, byte* cachedir);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_get_hookdirs")]
  internal static partial _alpm_list_t* alpm_option_get_hookdirs(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_add_hookdir")]
  internal static partial int alpm_option_add_hookdir(SafeAlpmHandle handle, byte* hookdir);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_remove_hookdir")]
  internal static partial int alpm_option_remove_hookdir(SafeAlpmHandle handle, byte* hookdir);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_get_overwrite_files")]
  internal static partial _alpm_list_t* alpm_option_get_overwrite_files(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_add_overwrite_file")]
  internal static partial int alpm_option_add_overwrite_file(SafeAlpmHandle handle, byte* glob);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_remove_overwrite_file")]
  internal static partial int alpm_option_remove_overwrite_file(SafeAlpmHandle handle, byte* glob);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_get_logfile")]
  internal static partial byte* alpm_option_get_logfile(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_set_logfile")]
  internal static partial int alpm_option_set_logfile(SafeAlpmHandle handle, byte* logfile);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_get_gpgdir")]
  internal static partial byte* alpm_option_get_gpgdir(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_set_gpgdir")]
  internal static partial int alpm_option_set_gpgdir(SafeAlpmHandle handle, byte* gpgdir);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_get_usesyslog")]
  internal static partial int alpm_option_get_usesyslog(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_set_usesyslog")]
  internal static partial int alpm_option_set_usesyslog(SafeAlpmHandle handle, int usesyslog);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_get_noupgrades")]
  internal static partial _alpm_list_t* alpm_option_get_noupgrades(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_add_noupgrade")]
  internal static partial int alpm_option_add_noupgrade(SafeAlpmHandle handle, byte* path);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_remove_noupgrade")]
  internal static partial int alpm_option_remove_noupgrade(SafeAlpmHandle handle, byte* path);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_get_noextracts")]
  internal static partial _alpm_list_t* alpm_option_get_noextracts(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_add_noextract")]
  internal static partial int alpm_option_add_noextract(SafeAlpmHandle handle, byte* path);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_remove_noextract")]
  internal static partial int alpm_option_remove_noextract(SafeAlpmHandle handle, byte* path);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_get_ignorepkgs")]
  internal static partial _alpm_list_t* alpm_option_get_ignorepkgs(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_add_ignorepkg")]
  internal static partial int alpm_option_add_ignorepkg(SafeAlpmHandle handle, byte* pkg);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_remove_ignorepkg")]
  internal static partial int alpm_option_remove_ignorepkg(SafeAlpmHandle handle, byte* pkg);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_get_ignoregroups")]
  internal static partial _alpm_list_t* alpm_option_get_ignoregroups(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_add_ignoregroup")]
  internal static partial int alpm_option_add_ignoregroup(SafeAlpmHandle handle, byte* grp);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_remove_ignoregroup")]
  internal static partial int alpm_option_remove_ignoregroup(SafeAlpmHandle handle, byte* grp);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_get_assumeinstalled")]
  internal static partial _alpm_list_t* alpm_option_get_assumeinstalled(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_add_assumeinstalled")]
  internal static partial int alpm_option_add_assumeinstalled(SafeAlpmHandle handle, _alpm_depend_t* dep);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_remove_assumeinstalled")]
  internal static partial int alpm_option_remove_assumeinstalled(SafeAlpmHandle handle, _alpm_depend_t* dep);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_get_architectures")]
  internal static partial _alpm_list_t* alpm_option_get_architectures(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_add_architecture")]
  internal static partial int alpm_option_add_architecture(SafeAlpmHandle handle, byte* arch);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_remove_architecture")]
  internal static partial int alpm_option_remove_architecture(SafeAlpmHandle handle, byte* arch);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_get_checkspace")]
  internal static partial int alpm_option_get_checkspace(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_set_checkspace")]
  internal static partial int alpm_option_set_checkspace(SafeAlpmHandle handle, int checkspace);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_get_dbext")]
  internal static partial byte* alpm_option_get_dbext(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_set_dbext")]
  internal static partial int alpm_option_set_dbext(SafeAlpmHandle handle, byte* dbext);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_get_default_siglevel")]
  internal static partial int alpm_option_get_default_siglevel(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_set_default_siglevel")]
  internal static partial int alpm_option_set_default_siglevel(SafeAlpmHandle handle, int level);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_get_local_file_siglevel")]
  internal static partial int alpm_option_get_local_file_siglevel(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_set_local_file_siglevel")]
  internal static partial int alpm_option_set_local_file_siglevel(SafeAlpmHandle handle, int level);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_get_remote_file_siglevel")]
  internal static partial int alpm_option_get_remote_file_siglevel(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_set_remote_file_siglevel")]
  internal static partial int alpm_option_set_remote_file_siglevel(SafeAlpmHandle handle, int level);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_get_parallel_downloads")]
  internal static partial int alpm_option_get_parallel_downloads(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_option_set_parallel_downloads")]
  internal static partial int alpm_option_set_parallel_downloads(SafeAlpmHandle handle, uint num_streams);

  [LibraryImport(__DllName, EntryPoint = "alpm_fetch_pkgurl")]
  internal static partial int alpm_fetch_pkgurl(SafeAlpmHandle handle, _alpm_list_t* urls, _alpm_list_t** fetched);

  [LibraryImport(__DllName, EntryPoint = "alpm_pkg_load")]
  internal static partial int alpm_pkg_load(SafeAlpmHandle handle, byte* filename, int full, int level, _alpm_pkg_t** pkg);

  [LibraryImport(__DllName, EntryPoint = "alpm_trans_get_flags")]
  internal static partial int alpm_trans_get_flags(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_trans_get_add")]
  internal static partial _alpm_list_t* alpm_trans_get_add(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_trans_get_remove")]
  internal static partial _alpm_list_t* alpm_trans_get_remove(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_trans_init")]
  internal static partial int alpm_trans_init(SafeAlpmHandle handle, int flags);

  [LibraryImport(__DllName, EntryPoint = "alpm_trans_prepare")]
  internal static partial int alpm_trans_prepare(SafeAlpmHandle handle, _alpm_list_t** data);

  [LibraryImport(__DllName, EntryPoint = "alpm_trans_commit")]
  internal static partial int alpm_trans_commit(SafeAlpmHandle handle, _alpm_list_t** data);

  [LibraryImport(__DllName, EntryPoint = "alpm_trans_interrupt")]
  internal static partial int alpm_trans_interrupt(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_trans_release")]
  internal static partial int alpm_trans_release(SafeAlpmHandle handle);

  [LibraryImport(__DllName, EntryPoint = "alpm_sync_sysupgrade")]
  internal static partial int alpm_sync_sysupgrade(SafeAlpmHandle handle, int enable_downgrade);

  [LibraryImport(__DllName, EntryPoint = "alpm_add_pkg")]
  internal static partial int alpm_add_pkg(SafeAlpmHandle handle, _alpm_pkg_t* pkg);

  [LibraryImport(__DllName, EntryPoint = "alpm_remove_pkg")]
  internal static partial int alpm_remove_pkg(SafeAlpmHandle handle, _alpm_pkg_t* pkg);

}
