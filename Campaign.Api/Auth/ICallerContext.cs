namespace Campaign.Api.Auth;

/// <summary>
/// Who is making the request. The controllers ask this and nothing else, so replacing the
/// implementation with one that reads JWT claims changes no controller.
/// </summary>
public interface ICallerContext
{
    /// <summary>The subject the grant records as its owner or as the actor who voided it.</summary>
    string ExternalUserId { get; }

    bool IsAdmin { get; }
}
