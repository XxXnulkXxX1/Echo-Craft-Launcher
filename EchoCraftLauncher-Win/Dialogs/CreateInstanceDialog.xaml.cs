using System.Windows;
using System.Windows.Controls;
using SwiftCraftLauncher2.Services;

namespace SwiftCraftLauncher2.Dialogs;

public partial class CreateInstanceDialog : Window
{
    private List<ManifestVersion> _all = new();

    public InstanceInfo? Created { get; private set; }

    public CreateInstanceDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _all = await App.Core.LoadVersionsAsync();
            ApplyFilter();
            Hint.Text = $"共 {_all.Count} 个版本，首次启动会自动下载游戏文件";
        }
        catch (Exception ex)
        {
            Hint.Text = "版本清单获取失败：" + ex.Message;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var keyword = SearchBox.Text.Trim();
        var releaseOnly = ReleaseOnly.IsChecked == true;
        var filtered = _all.Where(v =>
                (!releaseOnly || v.Type == "release")
                && (keyword.Length == 0 || v.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        VersionList.ItemsSource = filtered;
        if (filtered.Count > 0)
        {
            VersionList.SelectedIndex = 0;
        }
    }

    private async void Ok_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            SwiftCraftLauncher2.Dialogs.AppDialog.Alert("请输入实例名字。", "新建实例");
            return;
        }
        if (VersionList.SelectedItem is not ManifestVersion selected)
        {
            SwiftCraftLauncher2.Dialogs.AppDialog.Alert("请选择一个游戏版本。", "新建实例");
            return;
        }

        OkButton.IsEnabled = false;
        var loader = LoaderFabric.IsChecked == true ? "fabric" : LoaderQuilt.IsChecked == true ? "quilt" : "";
        try
        {
            var versionId = selected.Id;
            var loaderVersion = "";
            if (loader.Length > 0)
            {
                Hint.Text = $"正在获取 {loader} 加载器版本…";
                var loaders = loader == "fabric"
                    ? await MojangApi.GetFabricLoadersAsync(selected.Id, CancellationToken.None)
                    : await MojangApi.GetQuiltLoadersAsync(selected.Id, CancellationToken.None);
                if (loaders.Count == 0)
                {
                    throw new LauncherException($"{loader} 没有适配 {selected.Id} 的加载器版本");
                }
                loaderVersion = loaders[0];
                Hint.Text = $"正在写入 {loader} {loaderVersion} 安装档案…";
                versionId = await MojangApi.InstallLoaderProfileAsync(App.Core.Paths, selected.Id, loader, loaderVersion, CancellationToken.None);
            }

            Created = InstanceStore.Create(App.Core.Paths, name, versionId, loader, loaderVersion,
                loader.Length == 0 ? "原版生存" : $"{loader} 模组");
            DialogResult = true;
        }
        catch (Exception ex)
        {
            Hint.Text = "创建失败：" + ex.Message;
            SwiftCraftLauncher2.Dialogs.AppDialog.Alert(ex.Message, "新建实例", true);
        }
        finally
        {
            OkButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

