// src/Memoria.App/ViewModels/ClientRuleRowViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;

namespace Memoria.App.ViewModels;

public sealed partial class ClientRuleRowViewModel : ObservableObject
{
    public int Id { get; }
    [ObservableProperty] private string _keyword;
    [ObservableProperty] private int _priority;

    public ClientRuleRowViewModel(int id, string keyword, int priority)
    {
        Id = id;
        _keyword = keyword;
        _priority = priority;
    }
}
