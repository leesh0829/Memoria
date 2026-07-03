namespace Memoria.Core.Models;

public enum NoteType { Plain, Checklist, WeeklyReport }
public enum ItemKind { Task, Issue }
public enum ReportFormatKind { A, B }
public enum ThemeMode { Light, Dark, System }

public sealed class Group
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? ParentId { get; set; }
    public bool IsSystem { get; set; }
    public int SortOrder { get; set; }
    public string? Color { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Note
{
    public int Id { get; set; }
    public int? GroupId { get; set; }
    public NoteType Type { get; set; }
    public string? Title { get; set; }
    public string? Body { get; set; }
    public string BodyFormat { get; set; } = "plain";
    public DateOnly? LogDate { get; set; }
    public ReportFormatKind? ReportFormat { get; set; }
    public DateOnly? ReportWeekStart { get; set; }
    public bool Pinned { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ChecklistItem
{
    public int Id { get; set; }
    public int NoteId { get; set; }
    public ItemKind Kind { get; set; }
    public string Text { get; set; } = "";
    public bool Done { get; set; }
    public DateTimeOffset? DoneAt { get; set; }
    public int? ClientId { get; set; }
    public bool IsManual { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class Client
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class ClientRule
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public string Keyword { get; set; } = "";
    public int Priority { get; set; }
}
