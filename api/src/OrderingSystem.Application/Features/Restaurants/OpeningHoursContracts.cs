namespace OrderingSystem.Application.Features.Restaurants;

/// <summary>
/// One window on one day, in the restaurant's own wall-clock time.
/// </summary>
/// <param name="CloseTime">
/// May be earlier than <paramref name="OpenTime"/>, which means the window runs past midnight.
/// A kitchen open from noon until two in the morning is one window, not two — and the day it is
/// filed under is the day it opens.
/// </param>
public sealed record OpeningWindow(DayOfWeek Day, TimeOnly OpenTime, TimeOnly CloseTime);

/// <summary>
/// A restaurant's whole week, plus whether it is open at this moment.
/// </summary>
/// <param name="IsOpenNow">
/// Computed rather than stored, and included because it is the question an owner editing this
/// screen actually has. Hours that look right and a restaurant that is shut is the confusion this
/// answers on the spot.
/// </param>
public sealed record WeeklyHoursResponse(
    IReadOnlyList<OpeningWindow> Windows,
    bool IsOpenNow,
    bool IsClosedIndefinitely);

/// <summary>
/// The week, replaced whole.
///
/// <para>
/// One write rather than a row at a time. What is being edited is a week — whether two windows
/// clash, and whether anything is left at all, are questions about the set — so a per-row endpoint
/// would have to validate the same set anyway while letting a client build an invalid week one
/// request at a time.
/// </para>
/// </summary>
/// <param name="ConfirmClosedIndefinitely">
/// Required to send an empty week.
/// <para>
/// A restaurant with no hours is shut to customers forever, and that is a legitimate thing to want
/// — a kitchen closing for August. It is also what a screen produces when somebody deletes rows
/// one at a time without meaning to finish. The flag is how the API tells those two apart, rather
/// than trusting a confirmation dialog it cannot see.
/// </para>
/// </param>
public sealed record SetWeeklyHoursRequest(
    IReadOnlyList<OpeningWindow> Windows,
    bool ConfirmClosedIndefinitely);
