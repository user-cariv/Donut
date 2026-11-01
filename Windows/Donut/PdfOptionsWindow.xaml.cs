using System.Windows;

namespace Donut;

public partial class PdfOptionsWindow : Window
{
    public PdfMode SelectedMode =>
        MergeButton.IsChecked == true ? PdfMode.Merge : PdfMode.Separate;

    public PdfOptionsWindow()
    {
        InitializeComponent();
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
