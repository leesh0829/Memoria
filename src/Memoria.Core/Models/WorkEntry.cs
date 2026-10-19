namespace Memoria.Core.Models;

public sealed class WorkEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool Done { get; set; }
    public string[] Statuses { get; set; } = ["요청"];
    public DateTime? RequestedDate { get; set; } = DateTime.Today;
    public DateTime? ProcessingStart { get; set; }
    public DateTime? ProcessingEnd { get; set; }
    public string[] RequestTypes { get; set; } = [];
    public string[] Clients { get; set; } = [];
    public string[] WorkTypes { get; set; } = [];
    public string[] RequestMethods { get; set; } = [];
    public string Request { get; set; } = "";
    public string Cause { get; set; } = "";
    public string Action { get; set; } = "";
    public string Requester { get; set; } = "";
    public string Assignee { get; set; } = "";

    public bool IsVisibleOn(DateTime date) => RequestedDate?.Date == date.Date
        || (ProcessingStart is { } start && ProcessingEnd is { } end
            && start.Date <= date.Date && date.Date <= end.Date)
        || Statuses.Any(s => s is "요청" or "작업 중" or "검토 중" or "회의필요")
        || ProcessingStart is null || ProcessingEnd is null;

    // 여러 상태를 선택해도 한 행은 한 번만 나열한다. 별도 구분 상태를 우선한다.
    public string Section => Statuses.Contains("완료") ? "완료"
        : Statuses.Contains("회의필요") ? "회의필요"
        : Statuses.Contains("검토 중") ? "검토 중" : "요청 / 작업 중";
    public int StatusOrder => Section switch { "검토 중" => 2, "회의필요" => 3, "완료" => 4,
        _ => Statuses.Contains("요청") ? 0 : 1 };
}
