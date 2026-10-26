using System.Globalization;
using TeamLocum.Web.Domain;

namespace TeamLocum.Web.ViewModels;

/// <summary>Presentation rules shared by the views, kept out of Razor so they can be tested.</summary>
public static class UiHelpers
{
    private static readonly CultureInfo Uk = CultureInfo.GetCultureInfo("en-GB");

    // Day and month names come from one fixed culture so the text does not change with the ICU version of the machine
    // (en-GB abbreviates September differently in newer releases); the pattern itself is the UK day-month-year order.
    private static readonly CultureInfo Names = CultureInfo.InvariantCulture;

    public static string Badge(BookingStatus status) => status switch
    {
        BookingStatus.Open => "text-bg-primary",
        BookingStatus.Filled => "text-bg-success",
        BookingStatus.Completed => "text-bg-secondary",
        _ => "text-bg-dark"
    };

    public static string Badge(LocumStatus status) => status switch
    {
        LocumStatus.Active => "text-bg-success",
        LocumStatus.PendingReview => "text-bg-warning",
        LocumStatus.Suspended => "text-bg-danger",
        _ => "text-bg-dark"
    };

    public static string Badge(ClientStatus status) => status switch
    {
        ClientStatus.Accepted => "text-bg-success",
        ClientStatus.AwaitingReview => "text-bg-warning",
        ClientStatus.Suspended => "text-bg-danger",
        _ => "text-bg-dark"
    };

    public static string Badge(ApplicationStatus status) => status switch
    {
        ApplicationStatus.Pending => "text-bg-warning",
        ApplicationStatus.Accepted => "text-bg-success",
        ApplicationStatus.Declined => "text-bg-danger",
        _ => "text-bg-secondary"
    };

    public static string Label(ClientStatus status) => status == ClientStatus.AwaitingReview ? "Awaiting review" : status.ToString();

    public static string Label(LocumStatus status) => status == LocumStatus.PendingReview ? "Pending review" : status.ToString();

    public static string Money(decimal amount) => amount.ToString("C2", Uk);

    /// <summary>"Tue 20 Oct 2026, 09:00–17:00", or "Tue 20 Oct 2026 22:00 – Wed 21 Oct 06:00" across midnight.</summary>
    public static string Window(DateTime start, DateTime end) => start.Date == end.Date
        ? $"{start.ToString("ddd d MMM yyyy", Names)}, {start:HH:mm}–{end:HH:mm}"
        : $"{start.ToString("ddd d MMM yyyy", Names)} {start:HH:mm} – {end.ToString("ddd d MMM", Names)} {end:HH:mm}";

    public static string Date(DateOnly date) => date.ToString("ddd d MMM yyyy", Names);

    /// <summary>"17 Oct 09:30", for when something happened.</summary>
    public static string Moment(DateTime local) => local.ToString("d MMM HH:mm", Names);

    /// <summary>"17 Oct 2026".</summary>
    public static string Day(DateTimeOffset moment) => moment.ToString("d MMM yyyy", Names);

    public static string Hours(TimeSpan duration) =>
        duration.TotalHours % 1 == 0 ? $"{duration.TotalHours:0} h" : $"{duration.TotalHours:0.#} h";

    /// <summary>The value an HTML datetime-local input expects.</summary>
    public static string ForInput(DateTime? value) => value?.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
}
