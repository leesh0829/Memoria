// src/Memoria.App/ViewModels/ClientRowViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;

namespace Memoria.App.ViewModels;

public sealed partial class ClientRowViewModel : ObservableObject
{
    public int Id { get; }
    [ObservableProperty] private string _name;
    [ObservableProperty] private bool _enabled;
    public int SortOrder { get; set; }

    public ClientRowViewModel(int id, string name, bool enabled, int sortOrder)
    {
        Id = id;
        _name = name;
        _enabled = enabled;
        SortOrder = sortOrder;
    }
}
