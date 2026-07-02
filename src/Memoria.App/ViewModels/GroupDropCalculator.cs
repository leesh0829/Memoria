namespace Memoria.App.ViewModels;

public enum DropZone { Before, Into, After }

public static class GroupDropCalculator
{
    public static DropZone ZoneForOffset(double y, double rowHeight)
    {
        if (rowHeight <= 0) return DropZone.Into;
        var r = y / rowHeight;
        if (r < 0.25) return DropZone.Before;
        if (r > 0.75) return DropZone.After;
        return DropZone.Into;
    }

    // index=int.MaxValue → 목적지 형제 끝(MoveGroup에서 Clamp).
    public static (int? parentId, int index) Resolve(
        DropZone zone, int targetGroupId, int? targetParentId, int targetIndexAmongSiblings) => zone switch
    {
        DropZone.Into => (targetGroupId, int.MaxValue),
        DropZone.Before => (targetParentId, targetIndexAmongSiblings),
        _ => (targetParentId, targetIndexAmongSiblings + 1),
    };
}
