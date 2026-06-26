using Memoria.Core.Models;

namespace Memoria.Core.Classification;

public interface IClientClassifier
{
    /// 활성 고객사의 규칙만 대상으로, Priority 오름차순으로 평가하여
    /// 첫 키워드 포함(대소문자 무시) 매칭의 ClientId 반환. 없으면 null(미분류).
    int? Classify(string taskText, IEnumerable<ClientRule> rules, ISet<int> enabledClientIds);
}
