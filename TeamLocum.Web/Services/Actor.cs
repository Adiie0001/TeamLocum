using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TeamLocum.Web.Data;
using TeamLocum.Web.Domain;

namespace TeamLocum.Web.Services;

/// <summary>
/// Who is asking, resolved from the signed-in identity and the database. Every service method takes one, so
/// authorisation is decided in the service layer and not only by attributes on controllers.
/// </summary>
public sealed record Actor(
    string UserId,
    string Role,
    int? ClientId,
    ClientStatus? ClientStatus,
    int? LocumId,
    LocumStatus? LocumStatus)
{
    public bool IsAdmin => Role == Roles.Admin;
    public bool IsClient => Role == Roles.Client;
    public bool IsLocum => Role == Roles.Locum;

    /// <summary>A hospital whose account has been approved, or an administrator.</summary>
    public bool CanPostShifts => IsAdmin || (IsClient && ClientStatus == Domain.ClientStatus.Accepted);

    public bool CanWork => IsLocum && LocumStatus == Domain.LocumStatus.Active;
}

public interface IActorAccessor
{
    /// <summary>The signed-in actor, or null for anonymous visitors and accounts that hold no known role.</summary>
    Task<Actor?> GetAsync(CancellationToken ct = default);
}

public sealed class ActorAccessor(IHttpContextAccessor http, ApplicationDbContext db) : IActorAccessor
{
    private Actor? _resolved;
    private bool _done;

    public async Task<Actor?> GetAsync(CancellationToken ct = default)
    {
        if (_done)
        {
            return _resolved;
        }

        var user = http.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            _done = true;
            return null;
        }

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var role = Roles.All.FirstOrDefault(user.IsInRole);
        if (userId is null || role is null)
        {
            _done = true;
            return null;
        }

        // Admin wins if an account somehow holds several roles.
        role = user.IsInRole(Roles.Admin) ? Roles.Admin : role;

        int? clientId = null;
        ClientStatus? clientStatus = null;
        int? locumId = null;
        LocumStatus? locumStatus = null;

        if (role == Roles.Client)
        {
            var client = await db.Clients.AsNoTracking().Where(c => c.UserId == userId)
                .Select(c => new { c.Id, c.Status }).SingleOrDefaultAsync(ct);
            clientId = client?.Id;
            clientStatus = client?.Status;
        }
        else if (role == Roles.Locum)
        {
            var locum = await db.Locums.AsNoTracking().Where(l => l.UserId == userId)
                .Select(l => new { l.Id, l.Status }).SingleOrDefaultAsync(ct);
            locumId = locum?.Id;
            locumStatus = locum?.Status;
        }

        _resolved = new Actor(userId, role, clientId, clientStatus, locumId, locumStatus);
        _done = true;
        return _resolved;
    }
}

public static class ActorAccessorExtensions
{
    public static async Task<Actor> RequireAsync(this IActorAccessor accessor, CancellationToken ct = default) =>
        await accessor.GetAsync(ct) ?? throw new ForbiddenException("You need to sign in with an approved account.");
}
