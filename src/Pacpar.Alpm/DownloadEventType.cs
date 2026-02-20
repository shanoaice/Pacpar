using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm;

[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
public abstract class DownloadEventType
{
  public static unsafe DownloadEventType FromUnion(_alpm_download_event_type_t eventType, void* data)
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

  public unsafe class Init(_alpm_download_event_init_t* backingStruct) : DownloadEventType
  {
    public bool IsOptional => backingStruct->optional != 0;
  }

  public unsafe class Progress(_alpm_download_event_progress_t* backingStruct) : DownloadEventType
  {
    public CLong Downloaded => backingStruct->downloaded;
    public CLong Total => backingStruct->total;
  }

  public unsafe class Retry(_alpm_download_event_retry_t* backingStruct) : DownloadEventType
  {
    public bool WillResume => backingStruct->resume != 0;
  }

  public unsafe class Completed(_alpm_download_event_completed_t* backingStruct) : DownloadEventType
  {
    public CLong Total => backingStruct->total;
    public int Result => backingStruct->result;
    public bool IsSuccessful => Result == 0;
    public bool IsUpToDate => Result == 1;
    public bool IsError => Result == -1;
  }
}
