using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using TeamLocum.Web.Domain;
using TeamLocum.Web.Services;
using TeamLocum.Web.Services.Matching;

namespace TeamLocum.Web.ViewModels;

public sealed record HomeViewModel(Actor? Actor, IReadOnlyList<DashboardItem> Items);

public sealed record ErrorViewModel(string RequestId, string? CorrelationId, int Status)
{
    public string Title => Status switch
    {
        StatusCodes.Status404NotFound => "We could not find that page",
        StatusCodes.Status403Forbidden => "You do not have access to that",
        StatusCodes.Status429TooManyRequests => "Too many requests",
        >= 500 => "Something went wrong on our side",
        _ => "That request could not be completed"
    };
}

public sealed record BookingIndexViewModel(Actor Actor, BookingListQuery Query, PagedResult<BookingRow> Result);

public sealed record BookingDetailsViewModel(Actor Actor, BookingDetails Details, MatchResult? Matches);

public sealed record BookingFormViewModel(BookingInput Input, bool IsEdit, int? Id, IReadOnlyList<SelectListItem> Clients);

public sealed record LocumIndexViewModel(Actor Actor, IReadOnlyList<LocumRow> Rows, string? Search, LocumStatus? Status);

public sealed record LocumDetailsViewModel(Actor Actor, LocumRow Locum);

public sealed record LocumProfileViewModel(Actor Actor, LocumProfileInput Input);

public sealed record ClientIndexViewModel(IReadOnlyList<ClientRow> Rows, ClientStatus? Status);

public sealed record ClientProfileViewModel(Actor Actor, ClientProfileInput Input);

public sealed class HolidayInput
{
    [Required]
    public DateOnly Date { get; set; }

    [Required, StringLength(80)]
    public string Details { get; set; } = string.Empty;
}
