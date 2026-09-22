using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

/// <summary>
/// A download callback's payload (<c>alpm_download_event_type_t</c> and the data structures it
/// describes).
/// </summary>
/// <remarks>
/// Every case is a managed snapshot: its fields are copied out of libalpm's structure while the
/// callback runs, which is the only time that structure exists (probed: libalpm builds the payload
/// on the calling thread's stack and overwrites it as soon as the callback returns - report item
/// F9). Keeping a case and reading it after the callback is therefore safe, which the borrowed
/// views this replaces were not.
/// </remarks>
[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
public abstract class DownloadEventType
{
  internal static unsafe DownloadEventType FromUnion(_alpm_download_event_type_t eventType, void* data)
  {
    return eventType switch
    {
      _alpm_download_event_type_t.ALPM_DOWNLOAD_INIT => new Init((_alpm_download_event_init_t*)data),
      _alpm_download_event_type_t.ALPM_DOWNLOAD_PROGRESS => new Progress((_alpm_download_event_progress_t*)data),
      _alpm_download_event_type_t.ALPM_DOWNLOAD_RETRY => new Retry((_alpm_download_event_retry_t*)data),
      _alpm_download_event_type_t.ALPM_DOWNLOAD_COMPLETED => new Completed((_alpm_download_event_completed_t*)data),
      _ => throw new ArgumentException($"Unknown download event type: {eventType}"),
    };
  }

  public class Init : DownloadEventType
  {
    internal unsafe Init(_alpm_download_event_init_t* init)
    {
      IsOptional = init->optional != 0;
    }

    public bool IsOptional { get; }
  }

  public class Progress : DownloadEventType
  {
    internal unsafe Progress(_alpm_download_event_progress_t* progress)
    {
      Downloaded = progress->downloaded;
      Total = progress->total;
    }

    public CLong Downloaded { get; }
    public CLong Total { get; }
  }

  public class Retry : DownloadEventType
  {
    internal unsafe Retry(_alpm_download_event_retry_t* retry)
    {
      WillResume = retry->resume != 0;
    }

    public bool WillResume { get; }
  }

  public class Completed : DownloadEventType
  {
    internal unsafe Completed(_alpm_download_event_completed_t* completed)
    {
      Total = completed->total;
      Result = completed->result;
    }

    public CLong Total { get; }

    /// <summary>libalpm's raw result: 0 on success, 1 when the file was already up to date, -1 on error.</summary>
    public int Result { get; }

    public bool IsSuccessful => Result == 0;
    public bool IsUpToDate => Result == 1;
    public bool IsError => Result == -1;
  }
}
