using CampusTrack.Application.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace CampusTrack.Infrastructure.Identity;

/// <summary>Requires the caller to hold one named permission.</summary>
public class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }
    public PermissionRequirement(string permission) => Permission = permission;
}

/// <summary>
/// Applies a permission check to an endpoint:
/// <c>[HasPermission(Permissions.Students.Create)]</c>.
/// The policy name embeds the permission, so no policy has to be registered up front -
/// PermissionPolicyProvider materialises them on demand.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class HasPermissionAttribute : AuthorizeAttribute
{
    public HasPermissionAttribute(string permission) => Policy = $"{Permissions.Prefix}:{permission}";
}
