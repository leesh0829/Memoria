using Memoria.Core.Models;

namespace Memoria.App.ViewModels;

public sealed class TrashItemViewModel
{
    private readonly DateTimeOffset _now;

    public TrashItemViewModel(Note note, int retentionDays, DateTimeOffset now)
    {
        Note = note;
        RetentionDays = retentionDays;
        _now = now;
    }

    public Note Note { get; }
    public int RetentionDays { get; }

    public int Id => Note.Id;

    public string DisplayTitle =>
        string.IsNullOrWhiteSpace(Note.Title) ? "(제목 없음)" : Note.Title!;

    public DateTimeOffset? DeletedAt => Note.DeletedAt;

    public int DaysUntilPurge
    {
        get
        {
            if (Note.DeletedAt is not { } deletedAt) return RetentionDays;
            var purgeAt = deletedAt.AddDays(RetentionDays);
            var remaining = (purgeAt - _now).TotalDays;
            return remaining <= 0 ? 0 : (int)Math.Ceiling(remaining);
        }
    }

    public bool IsExpired => DaysUntilPurge <= 0;
}
