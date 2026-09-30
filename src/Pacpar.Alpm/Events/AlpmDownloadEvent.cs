using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

/// <summary>
/// A download callback's payload (<c>alpm_download_event_type_t</c> and the data structures it
/// describes).
/// </summary>
/// <remarks>
/// Every event subclass is a managed snapshot whose values are copied from libalpm during callback execution.
/// Instances are safe to persist or inspect after the callback completes.
/// </remarks>
[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
public abstract class AlpmDownloadEvent
{
  internal static unsafe AlpmDownloadEvent FromUnion(_alpm_download_event_type_t eventType, void* data)
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

  /// <summary>Download initialization event.</summary>
  public class Init : AlpmDownloadEvent
  {
    internal unsafe Init(_alpm_download_event_init_t* init)
    {
      IsOptional = init->optional != 0;
    }

    /// <summary>Gets whether the file being downloaded is optional.</summary>
    public bool IsOptional { get; }
  }

  /// <summary>Download progress event.</summary>
  public class Progress : AlpmDownloadEvent
  {
    internal unsafe Progress(_alpm_download_event_progress_t* progress)
    {
      Downloaded = progress->downloaded.Value;
      Total = progress->total.Value;
    }

    /// <summary>Gets the number of bytes downloaded so far.</summary>
    public long Downloaded { get; }
    /// <summary>Gets the expected total size of the download in bytes.</summary>
    public long Total { get; }
  }

  /// <summary>Download retry event.</summary>
  public class Retry : AlpmDownloadEvent
  {
    internal unsafe Retry(_alpm_download_event_retry_t* retry)
    {
      WillResume = retry->resume != 0;
    }

    /// <summary>Gets whether the retry attempt will resume downloading from the existing partial file.</summary>
    public bool WillResume { get; }
  }

  /// <summary>Download completion event.</summary>
  public class Completed : AlpmDownloadEvent
  {
    internal unsafe Completed(_alpm_download_event_completed_t* completed)
    {
      Total = completed->total.Value;
      Result = completed->result;
    }

    /// <summary>Gets the total size of the downloaded file in bytes.</summary>
    public long Total { get; }

    /// <summary>Gets libalpm's raw download result code: 0 on success, 1 when the file was already up to date, or -1 on error.</summary>
    public int Result { get; }

    /// <summary>Gets whether the download succeeded.</summary>
    public bool IsSuccessful => Result == 0;
    /// <summary>Gets whether the remote file was already up to date and did not need to be retrieved.</summary>
    public bool IsUpToDate => Result == 1;
    /// <summary>Gets whether the download encountered an error.</summary>
    public bool IsError => Result == -1;
  }
}
