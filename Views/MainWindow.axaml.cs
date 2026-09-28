using Avalonia.Controls;
using Avalonia.Interactivity;
using JapanomyApp.ViewModels;

namespace JapanomyApp.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnReadingSearchSelected(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.SearchTypeIndex = 0;
        }
    }

    private void OnKanjiSearchSelected(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.SearchTypeIndex = 1;
        }
    }
}