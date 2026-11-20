using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FluentAssertions;
using Memoria.App.Behaviors;

namespace Memoria.Tests.App;

public class ListDragSourceTests
{
    [Fact]
    public void OnlyActualItemContentCanStartReordering()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var label = new TextBlock();
                var item = new ListBoxItem { Content = label };
                ListDragSource.FindItem<ListBoxItem>(label).Should().BeSameAs(item);
                ListDragSource.FindItem<ListBoxItem>(new ScrollBar()).Should().BeNull();
                ListDragSource.FindItem<ListBoxItem>(new Thumb()).Should().BeNull();
                ListDragSource.FindItem<ListBoxItem>(new Border()).Should().BeNull();
                foreach (var control in new FrameworkElement[] { new Button(), new ScrollBar(), new Thumb() })
                {
                    item.Content = control;
                    ListDragSource.FindItem<ListBoxItem>(control).Should().BeNull();
                }
                item.Content = null;
                var treeItem = new TreeViewItem { Header = label };
                ListDragSource.FindItem<TreeViewItem>(treeItem).Should().BeSameAs(treeItem);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }
}
