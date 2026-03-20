using TTA.DataAccess.Models.Base;

namespace TTA.DataAccess.Models;

public class SportConfiguration : IKeyedEntity<Guid>
{
    public Guid Id { get; set; }
    public Guid SportId { get; set; }
    public bool UsesCleanTime { get; set; }
    public int PeriodsCount { get; set; }
    public int PeriodDurationMinutes { get; set; }
    public string? FieldSize { get; set; }
    public int RosterLimit { get; set; }
    public int LineupLimit { get; set; }
}