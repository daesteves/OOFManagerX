namespace OOFManagerX.Core.Models;

/// <summary>
/// Represents the complete OOF schedule configuration.
/// </summary>
public record OOFSchedule
{
    /// <summary>
    /// Working days schedule (7 days, Sunday = 0).
    /// </summary>
    public IReadOnlyList<WorkingDay> WorkingDays { get; init; } = CreateDefaultWeek();

    /// <summary>
    /// Primary OOF message for regular schedule.
    /// </summary>
    public OOFMessage PrimaryMessage { get; init; } = new();

    /// <summary>
    /// Extended/Secondary OOF message for vacation or extended absence.
    /// </summary>
    public OOFMessage ExtendedMessage { get; init; } = new();

    /// <summary>
    /// Whether extended OOF mode is currently active.
    /// </summary>
    public bool IsExtendedOOFActive { get; init; }

    /// <summary>
    /// Date when extended OOF ends and normal schedule resumes.
    /// </summary>
    public DateTime? ExtendedOOFEndDate { get; init; }

    /// <summary>
    /// Controls who receives external OOF messages.
    /// </summary>
    public ExternalAudienceScope ExternalAudience { get; init; } = ExternalAudienceScope.All;

    /// <summary>
    /// Creates a default week schedule (M-F working, weekends off).
    /// </summary>
    public static IReadOnlyList<WorkingDay> CreateDefaultWeek()
    {
        return Enum.GetValues<DayOfWeek>()
            .Select(WorkingDay.CreateDefault)
            .ToList()
            .AsReadOnly();
    }
}

/// <summary>
/// Defines who receives external OOF messages.
/// </summary>
public enum ExternalAudienceScope
{
    /// <summary>
    /// No external recipients receive OOF.
    /// </summary>
    None = 0,

    /// <summary>
    /// Only contacts receive OOF.
    /// </summary>
    ContactsOnly = 1,

    /// <summary>
    /// All external recipients receive OOF.
    /// </summary>
    All = 2
}
