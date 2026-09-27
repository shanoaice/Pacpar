namespace Pacpar.Alpm;

/// <summary>
/// Thrown when an operation attempts to access a borrowed view, database, or resource
/// whose underlying unmanaged memory has been invalidated or deallocated.
/// </summary>
/// <remarks>
/// Derives from <see cref="InvalidOperationException"/>: the object itself is intact, but the
/// operation is not valid in its current (released) state.
/// </remarks>
public sealed class AlpmLifetimeException : InvalidOperationException
{
  /// <summary>Creates the exception for the invalidated resource <paramref name="target"/>.</summary>
  /// <param name="target">The resource that was invalidated, e.g. <c>"the local database"</c>.</param>
  /// <param name="invalidatedBy">
  /// The operation that released it, e.g. <c>"Database.Unregister()"</c>; <c>null</c> when unknown.
  /// </param>
  public AlpmLifetimeException(string target, string? invalidatedBy)
    : base(BuildMessage(target, invalidatedBy))
  {
    Target = target;
    InvalidatedBy = invalidatedBy;
  }

  /// <summary>The target resource that was invalidated (e.g. 'the local database', 'the sync database core').</summary>
  public string Target { get; }

  /// <summary>The operation that caused the invalidation (e.g. 'Database.Unregister()', 'Transaction.Commit()').</summary>
  public string? InvalidatedBy { get; }

  private static string BuildMessage(string target, string? invalidatedBy)
  {
    var reason = invalidatedBy is null
      ? $"{target} is no longer valid"
      : $"{target} was released by {invalidatedBy}";

    return $"{reason}, so this object points into unmanaged memory that has been deallocated. "
         + "A borrowed resource or view remains valid only while its owning context is active: "
         + "use it within the active scope, or call ToSnapshot() before releasing the owner.";
  }
}
