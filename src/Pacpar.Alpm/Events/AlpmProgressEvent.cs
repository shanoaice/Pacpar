namespace Pacpar.Alpm;

/// <summary>
/// A progress callback's payload: one <c>alpm_cb_progress</c> invocation copied into a managed value.
/// </summary>
/// <remarks>
/// The struct is a snapshot. libalpm passes its arguments by value and keeps no reference to them, so
/// an instance stays safe to inspect after the callback returns.
/// <para>
/// The counters follow libalpm's own conventions, which are not uniform across progress types. For
/// package operations, <see cref="Current"/> is the 1-based index of the package being operated on and
/// <see cref="Percent"/> covers that package. For the checking phases (conflicts, disk space,
/// integrity, keyring and load), <see cref="Current"/> is a 0-based index and <see cref="Percent"/>
/// covers the whole phase. <see cref="PackageName"/> is empty during the checking phases.
/// </para>
/// <para>
/// This type is a readonly struct because progress is the most frequent callback: libalpm reports
/// once per file entry while it extracts or removes a package. A class payload would add one heap
/// object per callback.
/// </para>
/// </remarks>
public readonly struct AlpmProgressEvent
{
  internal AlpmProgressEvent(ProgressType type, string packageName, int percent, int current, int total)
  {
    Type = type;
    PackageName = packageName;
    Percent = percent;
    Current = current;
    Total = total;
  }

  /// <summary>Gets the kind of operation that is progressing (<c>_alpm_progress_t</c>).</summary>
  public ProgressType Type { get; }

  /// <summary>
  /// Gets the name of the package being operated on. Empty during the checking phases, which do not
  /// act on one package.
  /// </summary>
  public string PackageName { get; }

  /// <summary>
  /// Gets the completion percentage of the current step. See the type remarks for what the percentage
  /// covers in each phase.
  /// </summary>
  public int Percent { get; }

  /// <summary>
  /// Gets the index of the item being processed. libalpm counts package operations from 1 and the
  /// checking phases from 0.
  /// </summary>
  public int Current { get; }

  /// <summary>Gets the total number of items in the action.</summary>
  public int Total { get; }
}
