using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SafeSeal.App.ViewModels;

namespace SafeSeal.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void GalleryArea_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !vm.IsDetailPaneOpen || vm.IsBatchModeEnabled)
        {
            return;
        }

        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if (FindAncestor<ListBoxItem>(source) is not null)
        {
            return;
        }

        vm.CloseDetailCommand.Execute(null);
    }

    private static T? FindAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T typed)
            {
                return typed;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }
}
