using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Memoria.App.Services;
using Memoria.Core.Data;
using Memoria.Core.Models;
using Memoria.Core.Reporting;

namespace Memoria.App.ViewModels;

public partial class WorkEntriesViewModel : ObservableObject
{
    private readonly IWorkEntryRepository _repository;
    private readonly IClipboardService _clipboard;
    private readonly IConfirmationDialogService _dialogs;
    public ObservableCollection<WorkEntryRow> Rows { get; } = [];
    public ICollectionView Entries { get; }
    [ObservableProperty] private DateTime _selectedDate = DateTime.Today;
    [ObservableProperty] private WorkEntryRow? _selectedEntry;
    [ObservableProperty] private string _message = "변경 내용은 자동 저장됩니다. 복사는 표시된 업무를 상태순으로 복사합니다.";

    public WorkEntriesViewModel(IWorkEntryRepository repository, IClipboardService clipboard, IConfirmationDialogService dialogs)
    {
        _repository = repository;
        _clipboard = clipboard;
        _dialogs = dialogs;
        foreach (var entry in repository.GetAll()) Rows.Add(CreateRow(entry));
        Entries = CollectionViewSource.GetDefaultView(Rows);
        Entries.Filter = item => ((WorkEntryRow)item).ToModel().IsVisibleOn(SelectedDate);
        Entries.SortDescriptions.Add(new SortDescription(nameof(WorkEntryRow.StatusOrder), ListSortDirection.Ascending));
        Entries.GroupDescriptions.Add(new PropertyGroupDescription(nameof(WorkEntryRow.Section)));
    }

    private WorkEntryRow CreateRow(WorkEntry entry) => new(entry, row =>
    {
        try { _repository.Save(row.ToModel()); Message = "저장됨"; }
        catch (Exception ex) { Message = "저장 실패: " + ex.Message + " — 저장을 눌러 다시 시도하세요."; }
    });

    partial void OnSelectedDateChanged(DateTime value) => Refresh();

    [RelayCommand]
    public void Refresh()
    {
        if (Entries is IEditableCollectionView { IsAddingNew: true } or IEditableCollectionView { IsEditingItem: true }) return;
        Entries.Refresh();
    }

    [RelayCommand]
    private void Add()
    {
        var entry = new WorkEntry { RequestedDate = SelectedDate.Date };
        try
        {
            _repository.Save(entry);
            var row = CreateRow(entry);
            Rows.Add(row);
            SelectedEntry = row;
            Refresh();
        }
        catch (Exception ex) { Message = "추가 실패: " + ex.Message; }
    }

    [RelayCommand]
    private void Save()
    {
        try
        {
            foreach (var row in Rows) _repository.Save(row.ToModel());
            Message = "모든 업무를 저장했습니다.";
            Refresh();
        }
        catch (Exception ex) { Message = "저장 실패: " + ex.Message; }
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedEntry is not { } row || !_dialogs.Confirm("선택한 업무 내역을 삭제할까요?")) return;
        try { _repository.Delete(row.Id); Rows.Remove(row); }
        catch (Exception ex) { Message = "삭제 실패: " + ex.Message; }
    }

    [RelayCommand]
    private void Copy()
    {
        var entries = Rows.Select(r => r.ToModel()).Where(e => e.IsVisibleOn(SelectedDate)).ToList();
        _clipboard.SetText(WorkEntryClipboard.Render(entries));
        Message = $"{entries.Count}건 복사됨 — 엑셀에 붙여넣으세요.";
    }
}

public partial class WorkEntryChoice : ObservableObject
{
    public string Name { get; }
    [ObservableProperty] private bool _isSelected;
    public WorkEntryChoice(string name, bool selected) { Name = name; _isSelected = selected; }
}

public partial class WorkEntryRow : ObservableObject
{
    public string Id { get; }
    private readonly Action<WorkEntryRow> _save;
    [ObservableProperty] private bool _done;
    [ObservableProperty] private DateTime? _requestedDate;
    [ObservableProperty] private DateTime? _processingStart;
    [ObservableProperty] private DateTime? _processingEnd;
    [ObservableProperty] private string _request;
    [ObservableProperty] private string _cause;
    [ObservableProperty] private string _action;
    [ObservableProperty] private string _requester;
    [ObservableProperty] private string _assignee;
    public ObservableCollection<WorkEntryChoice> Statuses { get; }
    public ObservableCollection<WorkEntryChoice> RequestTypes { get; }
    public ObservableCollection<WorkEntryChoice> Clients { get; }
    public ObservableCollection<WorkEntryChoice> WorkTypes { get; }
    public ObservableCollection<WorkEntryChoice> RequestMethods { get; }
    public string Section => ToModel().Section;
    public int StatusOrder => ToModel().StatusOrder;

    public WorkEntryRow(WorkEntry entry, Action<WorkEntryRow> save)
    {
        Id = entry.Id; _save = save;
        _done = entry.Done; _requestedDate = entry.RequestedDate;
        _processingStart = entry.ProcessingStart; _processingEnd = entry.ProcessingEnd;
        _request = entry.Request; _cause = entry.Cause; _action = entry.Action;
        _requester = entry.Requester; _assignee = entry.Assignee;
        Statuses = Choices(["요청", "회의필요", "검토 중", "작업 중", "완료"], entry.Statuses);
        RequestTypes = Choices(["MES", "설비", "라벨프린트", "프로그램개발", "기타"], entry.RequestTypes);
        Clients = Choices(["SCA", "MTP", "ECI", "SLD_파주", "SLD_공주", "코모텍", "충북", "자율형공장"], entry.Clients);
        WorkTypes = Choices(["유지보수", "개발", "문서", "설계"], entry.WorkTypes);
        RequestMethods = Choices(["유선", "담당자 지정", "카카오톡(단체)", "카카오톡(개인)", "이메일", "현장"], entry.RequestMethods);
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Section) or nameof(StatusOrder)) return;
            _save(this);
        };
    }

    private ObservableCollection<WorkEntryChoice> Choices(string[] names, string[] selected)
    {
        var choices = new ObservableCollection<WorkEntryChoice>(names.Union(selected)
            .Select(n => new WorkEntryChoice(n, selected.Contains(n))));
        foreach (var choice in choices) choice.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Section)); OnPropertyChanged(nameof(StatusOrder)); _save(this);
        };
        return choices;
    }

    private static string[] Selected(IEnumerable<WorkEntryChoice> choices) => choices.Where(c => c.IsSelected).Select(c => c.Name).ToArray();
    public WorkEntry ToModel() => new()
    {
        Id = Id, Done = Done, RequestedDate = RequestedDate?.Date,
        ProcessingStart = ProcessingStart?.Date, ProcessingEnd = ProcessingEnd?.Date,
        Statuses = Selected(Statuses), RequestTypes = Selected(RequestTypes), Clients = Selected(Clients),
        WorkTypes = Selected(WorkTypes), RequestMethods = Selected(RequestMethods),
        Request = Request, Cause = Cause, Action = Action, Requester = Requester, Assignee = Assignee,
    };
}
