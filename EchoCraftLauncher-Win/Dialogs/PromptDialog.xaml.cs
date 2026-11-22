using System.Text.RegularExpressions;
using System.Windows;

namespace SwiftCraftLauncher2.Dialogs;

public partial class PromptDialog : Window
{
    private static readonly Regex NamePattern = new("^[A-Za-z0-9_]{1,16}$", RegexOptions.Compiled);

    public string Value { get; private set; } = "";

    public PromptDialog(string title, string hint, string initial)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        HintText.Text = hint;
        ValueBox.Text = initial;
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var value = ValueBox.Text.Trim();
        if (!NamePattern.IsMatch(value))
        {
            SwiftCraftLauncher2.Dialogs.AppDialog.Alert("用户名只能是 1–16 位字母、数字或下划线。", "离线用户名");
            return;
        }
        Value = value;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

