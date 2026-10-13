namespace Platform.Api.Catalog.Entities;

/// <summary>
/// A concrete run of an offering in time: a program cohort, a scheduled class or a bookable slot.
/// Holds the schedule and the current enrollment option.
/// </summary>
public class OfferingInstance : CatalogEntity
{
    public Guid OfferingId { get; set; }
    public Offering Offering { get; set; } = null!;

    /// <summary>e.g. "Fall 2026 cohort".</summary>
    public required string Name { get; set; }

    /// <summary>Null for rolling/evergreen enrollment.</summary>
    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public List<ScheduleRule> Schedule { get; set; } = [];

    public EnrollmentStatus EnrollmentStatus { get; set; } = EnrollmentStatus.ComingSoon;

    public DateTimeOffset? EnrollmentDeadline { get; set; }

    public int? Capacity { get; set; }

    public int? SeatsLeft { get; set; }
}

/// <summary>Recurring weekly slot, e.g. Tuesdays 18:00 for 60 minutes, America/New_York.</summary>
public class ScheduleRule
{
    public List<DayOfWeek> DaysOfWeek { get; set; } = [];

    public TimeOnly StartTime { get; set; }

    public int DurationMinutes { get; set; }

    /// <summary>IANA time zone of <see cref="StartTime"/>.</summary>
    public required string TimeZone { get; set; }
}
