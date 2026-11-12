using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkLogger.Core.Classification;

namespace WorkLogger.Desktop.ViewModels;

public sealed record IssueRow(string Key, string DurationText, double Share, IBrush Brush, string Topic, string Meta);

public sealed record AppRow(string Name, string DurationText, double Share, IBrush Brush);

public sealed record CommitRow(string Time, string Repo, string Branch, string Message, string Stats);

public sealed record HighlightRow(string Text, bool IsPositive, bool IsWarning);

public sealed record AssignmentRow(GroupRef Group, string Text, IRelayCommand RemoveCommand);

public sealed record PendingDay(DateOnly Day, string Text);

/// <summary>Sınıflandırılmamış bir iş grubu; satırdan bir issue'ya atanabilir.</summary>
public partial class UnclassifiedItem : ObservableObject
{
    private readonly Func<UnclassifiedItem, Task> _assign;
    private readonly Func<UnclassifiedItem, Task<string>> _preview;

    public UnclassifiedItem(Func<UnclassifiedItem, Task> assign, Func<UnclassifiedItem, Task<string>> preview)
    {
        _assign = assign;
        _preview = preview;
    }

    public required string Label { get; init; }
    public required string DurationText { get; init; }
    public required string Detail { get; init; }
    public GroupRef? Group { get; init; }
    public string? SuggestedKey { get; init; }
    public IReadOnlyList<string> QuickKeys { get; init; } = [];

    public bool CanAssign => Group is not null;
    public bool HasSuggestion => SuggestedKey is not null;
    public string SuggestionText => SuggestedKey is null ? "" : $"Öneri: {SuggestedKey}";

    [ObservableProperty] private string _keyInput = "";
    [ObservableProperty] private bool _createRule;
    [ObservableProperty] private string? _previewText;
    [ObservableProperty] private string? _error;

    partial void OnCreateRuleChanged(bool value)
    {
        if (value && PreviewText is null)
            _ = LoadPreviewAsync();
    }

    private async Task LoadPreviewAsync()
    {
        PreviewText = "Son 30 gün hesaplanıyor…";
        PreviewText = await _preview(this);
    }

    [RelayCommand]
    private void UseKey(string key) => KeyInput = key;

    [RelayCommand]
    private Task Assign() => _assign(this);
}

public partial class WorklogRow : ObservableObject
{
    public required string IssueKey { get; init; }
    public required string TrackedText { get; init; }
    public required IBrush Brush { get; init; }
    public bool CommitOnly { get; init; }

    /// <summary>İzlenmeden, kullanıcı tarafından eklenen satır.</summary>
    public bool Manual { get; init; }

    [ObservableProperty] private decimal? _minutes;
    [ObservableProperty] private string _start = "";
    [ObservableProperty] private string _description = "";

    /// <summary>Kullanıcı bu satırı düzenledi; değerler kalıcı taslaktan geliyor.</summary>
    [ObservableProperty] private bool _isEdited;

    public IRelayCommand? RemoveCommand { get; init; }

    public int MinutesValue => (int)(Minutes ?? 0);
}
