using System.Security.Claims;
using CampusTrack.Application.Authorization;
using CampusTrack.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace CampusTrack.Infrastructure.Identity;

/// <summary>
/// Creates a policy for any name of the form <c>permission:students.create</c> the first time
/// it is asked for, so adding a permission needs a constant and an attribute - never a
/// registration in startup that someone will forget.
/// </summary>
public class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider
{
    private readonly AuthorizationOptions _options;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : base(options)
        => _options = options.Value;

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        var existing = await base.GetPolicyAsync(policyName);
        if (existing is not null) return existing;

        if (!policyName.StartsWith($"{Permissions.Prefix}:", StringComparison.OrdinalIgnoreCase))
            return null;

        var permission = policyName[(Permissions.Prefix.Length + 1)..];
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission))
            .Build();

        // Cache it so the reflection-free fast path is used from the second request onward.
        _options.AddPolicy(policyName, policy);
        return policy;
    }
}

/// <summary>
/// Decides a permission requirement from the claims already on the token.
///
/// SuperAdmin passes everything by design - it is the break-glass role a school cannot lock
/// itself out of. Every other decision is a plain set membership test against the "perm"
/// claims minted at sign-in.
/// </summary>
public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true) return Task.CompletedTask;

        if (context.User.IsInRole(Permissions.RoleNames.SuperAdmin))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var granted = context.User.FindAll(CampusClaims.Permission)
            .Any(c => string.Equals(c.Value, requirement.Permission, StringComparison.OrdinalIgnoreCase));

        if (granted) context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
