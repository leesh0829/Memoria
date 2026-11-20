using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Memoria.App.Behaviors;

internal static class ListDragSource
{
    /// <summary>스크롤바/Thumb/버튼에서는 항목 드래그를 시작하지 않는다.</summary>
    public static T? FindItem<T>(object? source) where T : FrameworkElement
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is ScrollBar or Thumb or ButtonBase) return null;
            if (current is T item) return item;
            current = current is Visual or Visual3D
                ? VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }
}
