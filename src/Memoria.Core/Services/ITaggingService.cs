using Memoria.Core.Models;

namespace Memoria.Core.Services;

/// task 텍스트 변경 시 자동 분류를 적용(수동 교정 항목은 보호).
public interface ITaggingService
{
    /// item이 Task이고 IsManual=false이면 현재 규칙으로 ClientId 재계산하여 반환(변경된 item).
    /// Issue이거나 IsManual=true이면 그대로 반환.
    ChecklistItem ApplyAutoTag(ChecklistItem item);
}
