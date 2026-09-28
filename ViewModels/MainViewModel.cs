using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using JapanomyApp.Models;
using JapanomyApp.Services;

namespace JapanomyApp.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly DatabaseService _dbService = new();

    // 検索モード切り替え (0: 読み検索, 1: 漢字検索)
    private int _searchTypeIndex = 0;
    public int SearchTypeIndex
    {
        get => _searchTypeIndex;
        set
        {
            if (SetProperty(ref _searchTypeIndex, value))
            {
                OnPropertyChanged(nameof(IsReadingSearch));
                OnPropertyChanged(nameof(IsKanjiSearch));
            }
        }
    }

    public bool IsReadingSearch => SearchTypeIndex == 0;
    public bool IsKanjiSearch => SearchTypeIndex == 1;

    private string _searchPattern = "";
    public string SearchPattern
    {
        get => _searchPattern;
        set => SetProperty(ref _searchPattern, value);
    }

    public List<string> MatchModes { get; } = new() { "完全一致", "前方一致", "後方一致", "部分一致" };

    private int _selectedMatchModeIndex = 0;
    public int SelectedMatchModeIndex
    {
        get => _selectedMatchModeIndex;
        set => SetProperty(ref _selectedMatchModeIndex, value);
    }

    // 読み検索用オプション
    private bool _ignoreN = false;
    public bool IgnoreN
    {
        get => _ignoreN;
        set => SetProperty(ref _ignoreN, value);
    }

    private bool _ignoreChoon = false;
    public bool IgnoreChoon
    {
        get => _ignoreChoon;
        set => SetProperty(ref _ignoreChoon, value);
    }

    // 漢字検索用オプション (true: 漢字のみ / false: ひらがな入りOK)
    private bool _exactKanjiOnly = false;
    public bool ExactKanjiOnly
    {
        get => _exactKanjiOnly;
        set => SetProperty(ref _exactKanjiOnly, value);
    }

    public ObservableCollection<ReadingItem> SearchResults { get; } = new();

    private ReadingItem? _selectedItem;
    public ReadingItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
            {
                _ = LoadSenseAsync(value);
            }
        }
    }

    private string _selectedSense = "";
    public string SelectedSense
    {
        get => _selectedSense;
        set => SetProperty(ref _selectedSense, value);
    }

    private string _statusMessage = "";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public async Task ExecuteSearchAsync()
    {
        SearchResults.Clear();
        SelectedItem = null;
        SelectedSense = "";
        StatusMessage = "検索中...";

        if (string.IsNullOrWhiteSpace(SearchPattern))
        {
            StatusMessage = "検索キーワードを入力してください。";
            return;
        }

        try
        {
            List<ReadingItem> results;

            if (IsReadingSearch)
            {
                var options = new SearchOptions
                {
                    Mode = (MatchMode)SelectedMatchModeIndex,
                    IgnoreN = IgnoreN,
                    IgnoreChoon = IgnoreChoon
                };
                results = await _dbService.SearchAdvancedAsync(SearchPattern, options);
            }
            else
            {
                var options = new KanjiSearchOptions
                {
                    Mode = (MatchMode)SelectedMatchModeIndex,
                    ExactKanjiOnly = ExactKanjiOnly
                };
                results = await _dbService.SearchKanjiAsync(SearchPattern, options);
            }

            foreach (var item in results)
            {
                SearchResults.Add(item);
            }

            StatusMessage = $"検索結果: {SearchResults.Count:N0} 件";
        }
        catch (Exception ex)
        {
            StatusMessage = $"検索エラー: {ex.Message}";
        }
    }

    private async Task LoadSenseAsync(ReadingItem? item)
    {
        if (item == null) return;
        SelectedSense = "読み込み中...";
        SelectedSense = await _dbService.GetSenseAsync(item.EntSeq);
    }

    public async Task CopyToClipboardAsync()
    {
        if (SelectedItem == null) return;
        string copyText = $"{SelectedItem.Reading}";
        if (!string.IsNullOrEmpty(SelectedItem.Kanji) && SelectedItem.Kanji != "（表記なし）")
        {
            copyText += $" ({SelectedItem.Kanji})";
        }
        if (!string.IsNullOrEmpty(SelectedSense) && SelectedSense != "（意味データなし）" && SelectedSense != "読み込み中...")
        {
            copyText += $" : {SelectedSense}";
        }
        await SetClipboardTextAsync(copyText, "全体をコピーしました");
    }

    public async Task CopyKanjiToClipboardAsync()
    {
        if (SelectedItem == null || string.IsNullOrEmpty(SelectedItem.Kanji) || SelectedItem.Kanji == "（表記なし）")
            return;

        await SetClipboardTextAsync(SelectedItem.Kanji, $"漢字「{SelectedItem.Kanji}」をコピーしました");
    }

    private async Task SetClipboardTextAsync(string text, string successMessage)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            IClipboard? clipboard = desktop.MainWindow.Clipboard;
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(text);
                StatusMessage = successMessage;
            }
        }
    }
}