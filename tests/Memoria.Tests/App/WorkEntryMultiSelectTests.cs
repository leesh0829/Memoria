using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using FluentAssertions;
using Memoria.App.ViewModels;
using Memoria.App.Views;

namespace Memoria.Tests.App;

public class WorkEntryMultiSelectTests
{
    [Fact]
    public void PopupChoices_FollowRowItemsSource_WhenControlIsReused()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var control = new WorkEntryMultiSelect
                {
                    ItemsSource = new ObservableCollection<WorkEntryChoice>
                    {
                        new("요청", true), new("완료", false),
                    },
                };
                var popup = ((Grid)control.Content).Children.OfType<Popup>().Single();
                var list = (ItemsControl)((Border)popup.Child).Child;
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                list.Items.Cast<WorkEntryChoice>().Select(c => c.Name).Should().Equal("요청", "완료");
                control.Summary.Should().Be("요청 ▾");

                control.ItemsSource = new ObservableCollection<WorkEntryChoice> { new("보류", true) };
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                list.Items.Cast<WorkEntryChoice>().Should().ContainSingle().Which.Name.Should().Be("보류");
                control.Summary.Should().Be("보류 ▾");
            }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(20)).Should().BeTrue("the popup binding check must finish");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
