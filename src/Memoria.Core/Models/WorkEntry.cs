namespace Memoria.Core.Models;

public sealed class WorkEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool Done { get; set; }
    public static IReadOnlyList<string> StatusOptions { get; } = Array.AsReadOnly(
        new[] { "대기중", "협의중", "진행중", "검토중", "완료", "미진행" });
    public string[] Statuses { get; set; } = ["대기중"];
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
        || Statuses.Select(NormalizeStatus).Any(s => s is "대기중" or "협의중" or "진행중" or "검토중")
        || ProcessingStart is null || ProcessingEnd is null;

    public static string NormalizeStatus(string status) => status.Trim().Replace(" ", "") switch
    {
        "진행중" or "작업중" => "진행중",
        "검토중" => "검토중",
        "회의필요" or "협의중" => "협의중",
        "완료" or "완료(테스트필요)" => "완료",
        "무산" or "미진행" => "미진행",
        _ => "대기중",
    };

    // 기존 복수 상태 데이터도 한 섹션에만 표시한다. 새 작업상태 선택은 하나만 유지한다.
    public string Section
    {
        get
        {
            var statuses = Statuses.Select(NormalizeStatus).ToHashSet();
            foreach (var section in new[] { "완료", "미진행", "협의중", "검토중", "진행중", "대기중" })
                if (statuses.Contains(section)) return section;
            return "대기중";
        }
    }
    public int StatusOrder => Section switch
    {
        "대기중" => 0, "협의중" => 1, "진행중" => 2,
        "검토중" => 3, "완료" => 4, "미진행" => 5,
        _ => 0,
    };
}
