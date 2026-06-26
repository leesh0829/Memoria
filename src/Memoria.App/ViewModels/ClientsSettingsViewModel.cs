// src/Memoria.App/ViewModels/ClientsSettingsViewModel.cs
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Memoria.Core.Data;
using Memoria.Core.Models;

namespace Memoria.App.ViewModels;

public sealed partial class ClientsSettingsViewModel : ObservableObject
{
    private readonly IClientRepository _clients;

    public ObservableCollection<ClientRowViewModel> Clients { get; } = new();
    public ObservableCollection<ClientRuleRowViewModel> Rules { get; } = new();

    [ObservableProperty] private ClientRowViewModel? _selectedClient;

    public ClientsSettingsViewModel(IClientRepository clients)
    {
        _clients = clients;
        LoadClients();
    }

    private void LoadClients()
    {
        Clients.Clear();
        foreach (var c in _clients.GetAll().OrderBy(c => c.SortOrder))
            Clients.Add(new ClientRowViewModel(c.Id, c.Name, c.Enabled, c.SortOrder));
    }

    partial void OnSelectedClientChanged(ClientRowViewModel? value)
    {
        Rules.Clear();
        if (value is null)
            return;
        foreach (var r in _clients.GetRules().Where(r => r.ClientId == value.Id).OrderBy(r => r.Priority))
            Rules.Add(new ClientRuleRowViewModel(r.Id, r.Keyword, r.Priority));
    }

    [RelayCommand]
    private void AddClient(string name)
    {
        var nextOrder = Clients.Count == 0 ? 0 : Clients.Max(c => c.SortOrder) + 1;
        var id = _clients.Create(new Client { Name = name, SortOrder = nextOrder, Enabled = true });
        Clients.Add(new ClientRowViewModel(id, name, true, nextOrder));
    }

    [RelayCommand]
    private void DeleteClient(ClientRowViewModel row)
    {
        _clients.Delete(row.Id);
        Clients.Remove(row);
    }

    [RelayCommand]
    private void MoveUp(ClientRowViewModel row)
    {
        var index = Clients.IndexOf(row);
        if (index <= 0)
            return;
        Swap(index, index - 1);
    }

    [RelayCommand]
    private void MoveDown(ClientRowViewModel row)
    {
        var index = Clients.IndexOf(row);
        if (index < 0 || index >= Clients.Count - 1)
            return;
        Swap(index, index + 1);
    }

    // 표시순(sort_order)만 교체한다. client_rules.priority는 건드리지 않는다(§6.3).
    private void Swap(int a, int b)
    {
        var rowA = Clients[a];
        var rowB = Clients[b];

        (rowA.SortOrder, rowB.SortOrder) = (rowB.SortOrder, rowA.SortOrder);
        _clients.Update(new Client { Id = rowA.Id, Name = rowA.Name, Enabled = rowA.Enabled, SortOrder = rowA.SortOrder });
        _clients.Update(new Client { Id = rowB.Id, Name = rowB.Name, Enabled = rowB.Enabled, SortOrder = rowB.SortOrder });

        Clients.Move(a, b);
    }

    [RelayCommand]
    private void AddRule()
    {
        var nextPriority = Rules.Count == 0 ? 1 : Rules.Max(r => r.Priority) + 1;
        Rules.Add(new ClientRuleRowViewModel(0, "", nextPriority));
    }

    [RelayCommand]
    private void DeleteRule(ClientRuleRowViewModel rule) => Rules.Remove(rule);

    [RelayCommand]
    private void SaveRules()
    {
        if (SelectedClient is null)
            return;

        var rules = Rules.Select(r => new ClientRule
        {
            ClientId = SelectedClient.Id,
            Keyword = r.Keyword,
            Priority = r.Priority,
        });
        _clients.ReplaceRules(SelectedClient.Id, rules);
    }
}
