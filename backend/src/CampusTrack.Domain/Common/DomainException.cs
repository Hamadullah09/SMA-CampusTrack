namespace CampusTrack.Domain.Common;

/// <summary>
/// Thrown when a business rule is violated. The API layer translates this into a
/// 409/422 problem response carrying <see cref="Code"/> so clients can react
/// without parsing English text.
/// </summary>
public class DomainException : Exception
{
    public string Code { get; }

    public DomainException(string code, string message) : base(message) => Code = code;

    public static DomainException Conflict(string message) => new("conflict", message);
    public static DomainException Invalid(string message) => new("invalid_operation", message);
    public static DomainException NotAllowed(string message) => new("not_allowed", message);
}
