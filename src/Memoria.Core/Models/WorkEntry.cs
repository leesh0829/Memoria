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

    public static string NormalizeStatus(string status) => status.Trim().Replace(" ", "") switch
    {
        "진행중" or "작업중" => "작업 중",
        "검토중" => "검토 중",
        "대기중" or "대기" => "대기",
        "협의중" => "협의중",
        _ => status.Trim(),
    };

    // 기존 복수 상태 데이터도 한 섹션에만 표시한다. 새 작업상태 선택은 하나만 유지한다.
    public string Section
    {
        get
        {
            var statuses = Statuses.Select(NormalizeStatus).ToHashSet();
            foreach (var section in new[] { "완료", "회의필요", "검토 중", "협의중", "대기", "보류", "미진행", "무산" })
                if (statuses.Contains(section)) return section;
            return "요청 / 작업 중";
        }
    }
    public int StatusOrder => Section switch
    {
        "검토 중" => 2, "회의필요" => 3, "협의중" => 4, "대기" => 5,
        "보류" => 6, "미진행" => 7, "무산" => 8, "완료" => 9,
        _ => Statuses.Contains("요청") ? 0 : 1,
    };
}
