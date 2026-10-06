namespace Pacpar.Alpm;

/// <summary>
/// Per-handle policy for what the wrapper copies out of libalpm when it materializes a callback
/// payload.
/// </summary>
/// <remarks>
/// This is wrapper behavior, not a libalpm option: a setting that maps to an <c>alpm_option_*</c> call
/// belongs in <see cref="AlpmOptions"/> instead. The object belongs to one <see cref="Alpm"/> handle
/// and is read through that handle's <see cref="Pacpar.Alpm.Callback"/> instance, which is why it is
/// an instance property rather than a setting: two handles in one process can carry different
/// policies, and nothing in this library reads process-wide state.
/// <para>
/// Values are read when a payload is built - for a question, inside <c>alpm_trans_prepare</c> or
/// <c>alpm_trans_commit</c> - so set them before starting the transaction. The type holds no native
/// state and can outlive the handle.
/// </para>
/// </remarks>
public sealed class AlpmBindingConfig
{
  /// <summary>
  /// Whether package snapshots in question callback payload include file lists
  /// </summary>
  /// <remarks>
  /// Setting it to true will also copy the package's file list, the one part of a package that libalpm
  /// loads on demand (<c>INFRQ_FILES</c>). Enabling this option incurs significant performance penalty,
  /// especially on large, complex packages, and usually a question handler don't need that much detail,
  /// thus this is off by default.
  /// </remarks>
  public bool QuestionPayloadIncludeFiles { get; set; } = false;
}
