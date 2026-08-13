using Microsoft.AspNetCore.Authorization;

namespace Vastora.API.Authorization;

/// <summary>
/// Marker requirement for the "BusinessMember" policy: the caller must be allowed to act on
/// the Business named by the current request's {businessId} route value. See
/// <see cref="BusinessAccessAuthorizationHandler"/> for the actual scoping rules.
/// </summary>
public class BusinessMemberRequirement : IAuthorizationRequirement;
