// tests/Memoria.Tests/ViewModels/ClientsSettingsViewModelTests.cs
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Memoria.App.ViewModels;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Xunit;

namespace Memoria.Tests.ViewModels;

public class ClientsSettingsViewModelTests
{
    private sealed class FakeClientRepository : IClientRepository
    {
        public List<Client> Store { get; } = new();
        public List<ClientRule> RuleStore { get; } = new();
        private int _nextClientId = 1;
        private int _nextRuleId = 1;

        public int Create(Client client)
        {
            client.Id = _nextClientId++;
            Store.Add(client);
            return client.Id;
        }

        public void Update(Client client)
        {
            var existing = Store.First(c => c.Id == client.Id);
            existing.Name = client.Name;
            existing.SortOrder = client.SortOrder;
            existing.Enabled = client.Enabled;
        }

        public void Delete(int id) => Store.RemoveAll(c => c.Id == id);

        public IReadOnlyList<Client> GetAll(bool enabledOnly = false)
            => Store.Where(c => !enabledOnly || c.Enabled).OrderBy(c => c.SortOrder).ToList();

        public IReadOnlyList<ClientRule> GetRules() => RuleStore.ToList();

        public void ReplaceRules(int clientId, IEnumerable<ClientRule> rules)
        {
            RuleStore.RemoveAll(r => r.ClientId == clientId);
            foreach (var r in rules)
            {
                r.Id = _nextRuleId++;
                r.ClientId = clientId;
                RuleStore.Add(r);
            }
        }
    }

    private static (ClientsSettingsViewModel vm, FakeClientRepository repo) Create()
    {
        var repo = new FakeClientRepository();
        // 표시순(sort_order)과 매칭순(priority)을 의도적으로 다르게 시드.
        repo.Create(new Client { Name = "SLD", SortOrder = 0, Enabled = true });          // id 1
        repo.Create(new Client { Name = "MTP", SortOrder = 1, Enabled = true });          // id 2
        repo.Create(new Client { Name = "자율형 공장", SortOrder = 2, Enabled = true });   // id 3
        repo.RuleStore.Add(new ClientRule { Id = 1, ClientId = 3, Keyword = "자율형공장", Priority = 1 });
        repo.RuleStore.Add(new ClientRule { Id = 2, ClientId = 1, Keyword = "SLD", Priority = 6 });
        var vm = new ClientsSettingsViewModel(repo);
        return (vm, repo);
    }

    [Fact]
    public void Loads_clients_ordered_by_sort_order()
    {
        var (vm, _) = Create();
        vm.Clients.Select(c => c.Name).Should().ContainInOrder("SLD", "MTP", "자율형 공장");
    }

    [Fact]
    public void AddClient_appends_with_next_sort_order()
    {
        var (vm, repo) = Create();

        vm.AddClientCommand.Execute("카본센스");

        var added = repo.Store.Single(c => c.Name == "카본센스");
        added.SortOrder.Should().Be(3); // max(0,1,2)+1
        vm.Clients.Last().Name.Should().Be("카본센스");
    }

    [Fact]
    public void DeleteClient_removes_from_repo_and_list()
    {
        var (vm, repo) = Create();
        var mtp = vm.Clients.Single(c => c.Name == "MTP");

        vm.DeleteClientCommand.Execute(mtp);

        repo.Store.Should().NotContain(c => c.Name == "MTP");
        vm.Clients.Should().NotContain(c => c.Name == "MTP");
    }

    [Fact]
    public void MoveDown_swaps_display_sort_order_only()
    {
        var (vm, repo) = Create();
        var sld = vm.Clients.Single(c => c.Name == "SLD");

        vm.MoveDownCommand.Execute(sld);

        // 표시순 변경: SLD <-> MTP
        vm.Clients.Select(c => c.Name).Should().ContainInOrder("MTP", "SLD", "자율형 공장");
        repo.Store.Single(c => c.Name == "MTP").SortOrder.Should().Be(0);
        repo.Store.Single(c => c.Name == "SLD").SortOrder.Should().Be(1);
    }

    [Fact]
    public void Reordering_display_does_not_change_rule_priority()
    {
        var (vm, repo) = Create();
        var sld = vm.Clients.Single(c => c.Name == "SLD");

        vm.MoveDownCommand.Execute(sld);

        // §6.3: 표시순 변경은 client_rules.priority에 영향 없음
        repo.RuleStore.Single(r => r.Keyword == "자율형공장").Priority.Should().Be(1);
        repo.RuleStore.Single(r => r.Keyword == "SLD").Priority.Should().Be(6);
    }

    [Fact]
    public void Selecting_client_loads_its_rules()
    {
        var (vm, _) = Create();
        vm.SelectedClient = vm.Clients.Single(c => c.Name == "자율형 공장");

        vm.Rules.Select(r => r.Keyword).Should().ContainSingle().Which.Should().Be("자율형공장");
    }

    [Fact]
    public void SaveRules_persists_priority_independently_of_sort_order()
    {
        var (vm, repo) = Create();
        vm.SelectedClient = vm.Clients.Single(c => c.Name == "자율형 공장");
        vm.Rules.Single().Priority = 1;
        vm.AddRuleCommand.Execute(null);
        vm.Rules.Last().Keyword = "자율형 공장";
        vm.Rules.Last().Priority = 2;

        vm.SaveRulesCommand.Execute(null);

        var saved = repo.RuleStore.Where(r => r.ClientId == 3).OrderBy(r => r.Priority).ToList();
        saved.Select(r => (r.Keyword, r.Priority)).Should()
            .ContainInOrder(("자율형공장", 1), ("자율형 공장", 2));
        // sort_order는 그대로
        repo.Store.Single(c => c.Id == 3).SortOrder.Should().Be(2);
    }
}
