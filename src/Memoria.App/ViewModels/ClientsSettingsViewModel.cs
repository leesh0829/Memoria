// src/Memoria.App/ViewModels/ClientsSettingsViewModel.cs
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Memoria.App.Services;
using Memoria.Core.Data;
using Memoria.Core.Models;

namespace Memoria.App.ViewModels;

public sealed partial class ClientsSettingsViewModel : ObservableObject
{
    private readonly IClientRepository _clients;

    public ObservableCollection<ClientRowViewModel> Clients { get; } = new();
    public ObservableCollection<ClientRuleRowViewModel> Rules { get; } = new();

    [ObservableProperty] private ClientRowViewModel? _selectedClient;
    private int _rulesLoadGeneration;

    public ClientsSettingsViewModel(IClientRepository clients)
    {
        _clients = clients;
        // 단위 테스트에는 WPF Dispatcher가 없으므로 기존 동기 계약을 유지한다.
        // 실제 앱에서는 SettingsWindow.Loaded가 비동기 로드를 호출한다.
        if (!CanUseAsyncUi()) LoadClients();
    }

    private void LoadClients()
    {
        ApplyClients(_clients.GetAll());
    }

    public async Task LoadClientsAsync()
    {
        var clients = await Task.Run(() => _clients.GetAll());
        ApplyClients(clients);
    }

    private void ApplyClients(IReadOnlyList<Client> clients)
    {
        Clients.Clear();
        foreach (var c in clients)   // GetAll은 이미 sort_order 정렬됨
            Clients.Add(new ClientRowViewModel(c.Id, c.Name, c.Enabled, c.SortOrder));
    }

    partial void OnSelectedClientChanged(ClientRowViewModel? value)
    {
        var generation = ++_rulesLoadGeneration;
        Rules.Clear();
        if (value is null)
            return;

        if (CanUseAsyncUi())
        {
            _ = LoadRulesAsync(value.Id, generation);
            return;
        }

        ApplyRules(_clients.GetRules(), value.Id, generation);
    }

    private async Task LoadRulesAsync(int clientId, int generation)
    {
        try
        {
            var rules = await Task.Run(() => _clients.GetRules());
            ApplyRules(rules, clientId, generation);
        }
        catch (System.Exception ex)
        {
            AppLog.Error("Clients.LoadRules", ex);
        }
    }

    private void ApplyRules(IReadOnlyList<ClientRule> rules, int clientId, int generation)
    {
        if (generation != _rulesLoadGeneration || SelectedClient?.Id != clientId) return;
        Rules.Clear();
        foreach (var r in rules.Where(r => r.ClientId == clientId).OrderBy(r => r.Priority))
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
    private async Task AddClientWithLoadingAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var nextOrder = Clients.Count == 0 ? 0 : Clients.Max(c => c.SortOrder) + 1;
        var client = new Client { Name = name, SortOrder = nextOrder, Enabled = true };
        try
        {
            var id = await Task.Run(() => _clients.Create(client));
            Clients.Add(new ClientRowViewModel(id, name, true, nextOrder));
        }
        catch (System.Exception ex)
        {
            AppLog.Error("Clients.Add", ex);
        }
    }

    [RelayCommand]
    private void DeleteClient(ClientRowViewModel row)
    {
        _clients.Delete(row.Id);
        Clients.Remove(row);
    }

    [RelayCommand]
    private async Task DeleteClientWithLoadingAsync(ClientRowViewModel? row)
    {
        if (row is null) return;
        try
        {
            await Task.Run(() => _clients.Delete(row.Id));
            Clients.Remove(row);
        }
        catch (System.Exception ex)
        {
            AppLog.Error("Clients.Delete", ex);
        }
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

    [RelayCommand]
    private Task MoveUpWithLoadingAsync(ClientRowViewModel? row)
    {
        if (row is null) return Task.CompletedTask;
        var index = Clients.IndexOf(row);
        return index <= 0 ? Task.CompletedTask : SwapAsync(index, index - 1);
    }

    [RelayCommand]
    private Task MoveDownWithLoadingAsync(ClientRowViewModel? row)
    {
        if (row is null) return Task.CompletedTask;
        var index = Clients.IndexOf(row);
        return index < 0 || index >= Clients.Count - 1
            ? Task.CompletedTask
            : SwapAsync(index, index + 1);
    }

    private async Task SwapAsync(int a, int b)
    {
        var rowA = Clients[a];
        var rowB = Clients[b];
        var orderA = rowB.SortOrder;
        var orderB = rowA.SortOrder;
        var modelA = new Client { Id = rowA.Id, Name = rowA.Name, Enabled = rowA.Enabled, SortOrder = orderA };
        var modelB = new Client { Id = rowB.Id, Name = rowB.Name, Enabled = rowB.Enabled, SortOrder = orderB };
        try
        {
            await Task.Run(() =>
            {
                _clients.Update(modelA);
                _clients.Update(modelB);
            });
            rowA.SortOrder = orderA;
            rowB.SortOrder = orderB;
            Clients.Move(a, b);
        }
        catch (System.Exception ex)
        {
            AppLog.Error("Clients.Reorder", ex);
        }
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

    [RelayCommand]
    private async Task SaveRulesWithLoadingAsync()
    {
        if (SelectedClient is null) return;
        var clientId = SelectedClient.Id;
        var rules = Rules.Select(r => new ClientRule
        {
            ClientId = clientId,
            Keyword = r.Keyword,
            Priority = r.Priority,
        }).ToList();
        try
        {
            await Task.Run(() => _clients.ReplaceRules(clientId, rules));
        }
        catch (System.Exception ex)
        {
            AppLog.Error("Clients.SaveRules", ex);
        }
    }

    private static bool CanUseAsyncUi()
        => Application.Current?.Dispatcher is { } dispatcher && dispatcher.CheckAccess();
}
