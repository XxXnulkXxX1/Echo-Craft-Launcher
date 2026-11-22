using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SwiftCraftLauncher2.Dialogs;
using SwiftCraftLauncher2.Services;

namespace SwiftCraftLauncher2.Pages;

public partial class DownloadPage : UserControl
{
    private const int ModPageSize = 30;

    private List<ManifestVersion> _all = new();
    private List<ModHit> _mods = new();
    private readonly DispatcherTimer _modDebounce;
    private CancellationTokenSource? _modCts;
    private CancellationTokenSource? _detailCts;
    private ModDetail? _detail;

    // 详情页原文 / 译文缓存，用于「查看原文」切换
    private string _bodyOriginal = "";
    private string? _bodyTranslated;
    private string _descOriginal = "";
    private string? _descTranslated;
    private bool _showingOriginal;
    private bool _loaded;
    private bool _modBusy;
    private int _modOffset;
    private bool _autoOpenFirstDetail;

    public DownloadPage()
    {
        InitializeComponent();
        _modDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _modDebounce.Tick += (_, _) =>
        {
            _modDebounce.Stop();
            _ = SearchModsAsync();
        };
        Loaded += OnLoaded;
        // 便于逐页核对：--tab=mods 时直接打开模组面板
        Loaded += (_, _) =>
        {
            var tab = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--tab=", StringComparison.OrdinalIgnoreCase));
            if (tab is not null && tab["--tab=".Length..].Equals("mods", StringComparison.OrdinalIgnoreCase))
            {
                TabMod.IsChecked = true;
            }
            // 调试用：--open-first=1 自动打开列表第一个模组的详情（用于核对翻译与排版）
            if (Environment.GetCommandLineArgs().Any(a => a.Equals("--open-first=1", StringComparison.OrdinalIgnoreCase)))
            {
                _autoOpenFirstDetail = true;
            }
        };
        Unloaded += (_, _) =>
        {
            _modDebounce.Stop();
            _modCts?.Cancel();
            _detailCts?.Cancel();
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshInstalled();
        RefreshTargets();
        if (!_loaded)
        {
            _loaded = true;
            await LoadVersionsAsync(false);
        }
        if (TabMod.IsChecked == true)
        {
            await SearchModsAsync();
        }
    }

    private void Tab_Changed(object sender, RoutedEventArgs e)
    {
        if (VersionPanel is null || ModPanel is null)
        {
            return;
        }
        var mod = TabMod.IsChecked == true;
        VersionPanel.Visibility = mod ? Visibility.Collapsed : Visibility.Visible;
        ModPanel.Visibility = mod ? Visibility.Visible : Visibility.Collapsed;
        if (mod && _mods.Count == 0 && !_modBusy)
        {
            _ = SearchModsAsync();
        }
    }

    // ---------------------------------------------------------------- 版本

    private async Task LoadVersionsAsync(bool force)
    {
        StatusText.Text = "正在获取版本清单…";
        RefreshButton.IsEnabled = false;
        try
        {
            _all = await App.Core.LoadVersionsAsync(force);
            ApplyFilter();
            StatusText.Text = $"共 {_all.Count} 个版本";
        }
        catch (Exception ex)
        {
            StatusText.Text = "获取失败：" + ex.Message;
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        if (VersionList is null)
        {
            return;
        }
        var keyword = SearchBox.Text.Trim();
        var releaseOnly = ReleaseOnly.IsChecked == true;
        var filtered = _all.Where(v =>
                (!releaseOnly || v.Type == "release")
                && (keyword.Length == 0 || v.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        VersionList.ItemsSource = filtered;
    }

    private void VersionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VersionList.SelectedItem is not ManifestVersion version)
        {
            DetailTitle.Text = "未选择";
            VersionDetailBody.Text = "";
            return;
        }
        DetailTitle.Text = version.Id;
        VersionDetailBody.Text = $"类型：{version.TypeLabel}\n发布时间：{version.ReleaseTime}\n\n安装后首次启动需要联网下载客户端与资源文件。";
    }

    private void RefreshInstalled()
    {
        var ids = App.Core.InstalledVersionIds().OrderByDescending(id => id).ToList();
        InstalledList.ItemsSource = ids;
        if (ids.Count == 0)
        {
            StatusText.Text = "本地还没有已安装的版本";
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = LoadVersionsAsync(true);

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateInstanceDialog { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
        {
            App.Core.RefreshInstances();
            RefreshInstalled();
            RefreshTargets();
            StatusText.Text = dialog.Created is null ? "已创建实例" : $"已创建实例：{dialog.Created.Name}";
        }
    }

    // ---------------------------------------------------------------- 模组

    private void RefreshTargets()
    {
        var instances = App.Core.Instances.Where(i => i.Loader.Length > 0).ToList();
        TargetList.ItemsSource = instances;
        DetailTargetBox.ItemsSource = instances;
        if (instances.Count == 0)
        {
            TargetHint.Text = "还没有 Fabric / Quilt 实例。可以先浏览全站模组，安装前需在「游戏版本」里新建一个带模组加载器的实例。";
            InstallModButton.IsEnabled = false;
            DetailInstallButton.IsEnabled = false;
            // 没有实例时无法按实例筛选，勾着会让用户误以为结果已经过滤过
            CompatOnly.IsChecked = false;
            CompatOnly.IsEnabled = false;
            return;
        }
        CompatOnly.IsEnabled = true;
        if (CompatOnly.IsChecked != true)
        {
            CompatOnly.IsChecked = true;
        }
        var last = App.Core.Config.LastInstance;
        var pick = instances.FirstOrDefault(i => i.Name == last) ?? instances[0];
        TargetList.SelectedItem = pick;
    }

    private void TargetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TargetList.SelectedItem is not InstanceInfo instance)
        {
            TargetHint.Text = "未选择实例：正在显示全站模组；选择实例后才会按加载器与版本筛选。";
            InstallModButton.IsEnabled = false;
            DetailInstallButton.IsEnabled = false;
            _ = SearchModsAsync();
            return;
        }
        var game = BaseGameVersion(instance);
        TargetHint.Text = $"目标：{instance.Name}（{game} / {instance.LoaderLabel}）";
        if (DetailTargetBox.SelectedItem != instance)
        {
            DetailTargetBox.SelectedItem = instance;
        }
        InstallModButton.IsEnabled = ModList.SelectedItem is ModHit && !_modBusy;
        DetailInstallButton.IsEnabled = _detail is not null && !_modBusy;
        _ = SearchModsAsync();
    }

    private void DetailTarget_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (DetailTargetBox.SelectedItem is InstanceInfo instance && TargetList.SelectedItem != instance)
        {
            TargetList.SelectedItem = instance;
        }
        DetailInstallButton.IsEnabled = _detail is not null && DetailTargetBox.SelectedItem is InstanceInfo && !_modBusy;
    }

    private void Compat_Changed(object sender, RoutedEventArgs e)
    {
        if (ModList is null)
        {
            return;
        }
        _ = SearchModsAsync();
    }

    /// <summary>分类胶囊：单选语义，点击一个时取消其余选中。</summary>
    private void CategoryChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton clicked)
        {
            return;
        }
        foreach (var child in CategoryPanel.Children)
        {
            if (child is ToggleButton chip && !ReferenceEquals(chip, clicked))
            {
                chip.IsChecked = false;
            }
        }
        if (clicked.IsChecked != true)
        {
            // 再次点击当前分类时保持选中，避免出现一个都不选的状态
            clicked.IsChecked = true;
        }
        if (_loaded)
        {
            _ = SearchModsAsync();
        }
    }

    private string CategoryTag()
    {
        foreach (var child in CategoryPanel.Children)
        {
            if (child is ToggleButton chip && chip.IsChecked == true)
            {
                return chip.Tag as string ?? "";
            }
        }
        return "";
    }

    /// <summary>从实例的版本链取出原版游戏版本号，用于筛选兼容模组。</summary>
    private static string BaseGameVersion(InstanceInfo instance)
    {
        try
        {
            var resolved = VersionResolver.Resolve(instance.VersionId, App.Core.Paths);
            var baseElement = resolved.Chain[^1];
            var id = VersionResolver.Str(baseElement, "id");
            return id.Length > 0 ? id : instance.VersionId;
        }
        catch
        {
            return instance.VersionId;
        }
    }

    private void ModSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_modDebounce is null)
        {
            return;
        }
        if (ModSearchHint is not null)
        {
            ModSearchHint.Visibility = ModSearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        _modDebounce.Stop();
        _modDebounce.Start();
    }

    private async Task SearchModsAsync(bool append = false)
    {
        if (_modBusy)
        {
            return;
        }
        _modCts?.Cancel();
        _modCts = new CancellationTokenSource();
        var token = _modCts.Token;

        var target = TargetList.SelectedItem as InstanceInfo;
        var compat = CompatOnly.IsChecked == true && target is not null;
        var loader = compat ? target!.Loader : "";
        var game = compat ? BaseGameVersion(target!) : "";
        var query = ModSearchBox.Text.Trim();
        var category = CategoryTag();

        if (!append)
        {
            _modOffset = 0;
        }

        ModEmpty.Visibility = Visibility.Visible;
        ModEmpty.Text = query.Length == 0 ? "正在加载热门模组…" : "正在搜索…";
        LoadMoreButton.Visibility = Visibility.Collapsed;
        ModStatus.Text = compat
            ? $"按 {game} / {target!.LoaderLabel} 筛选 Modrinth…"
            : "未限定实例，显示全站热门模组…";

        try
        {
            var hits = await Modrinth.SearchAsync(query, loader, game, category, ModPageSize, _modOffset, token);
            token.ThrowIfCancellationRequested();
            if (append)
            {
                _mods.AddRange(hits);
            }
            else
            {
                _mods = hits;
            }
            _modOffset += hits.Count;
            ModList.ItemsSource = null;
            ModList.ItemsSource = _mods.ToList();

            if (_mods.Count == 0)
            {
                ModStatus.Text = compat
                    ? $"没有找到适配 {game} / {target!.LoaderLabel} 的模组，可取消勾选兼容过滤查看全站结果。"
                    : "没有找到相关模组，换个关键词或分类试试。";
                ModEmpty.Text = "暂无结果";
                ModEmpty.Visibility = Visibility.Visible;
            }
            else
            {
                ModStatus.Text = compat ? $"已显示 {_mods.Count} 个兼容模组" : $"已显示 {_mods.Count} 个模组";
                ModEmpty.Visibility = Visibility.Collapsed;
                if (!append)
                {
                    ModList.SelectedIndex = 0;
                }
            }
            LoadMoreButton.Visibility = hits.Count >= ModPageSize ? Visibility.Visible : Visibility.Collapsed;
            _ = LoadIconsAsync(hits, token);

            // 调试参数：自动打开第一条详情，便于核对详情页与翻译
            if (_autoOpenFirstDetail && !append && _mods.Count > 0)
            {
                _autoOpenFirstDetail = false;
                _ = ShowDetailAsync(_mods[0]);
            }
        }
        catch (OperationCanceledException)
        {
            // 被下一次搜索取代，忽略
        }
        catch (Exception ex)
        {
            _mods = new List<ModHit>();
            ModList.ItemsSource = null;
            ModStatus.Text = "搜索失败：" + ex.Message;
            ModEmpty.Text = "搜索失败，检查网络后重试";
            ModEmpty.Visibility = Visibility.Visible;
        }
    }

    private void LoadMore_Click(object sender, RoutedEventArgs e)
    {
        if (_modBusy)
        {
            return;
        }
        _ = SearchModsAsync(true);
    }

    /// <summary>后台并发下载图标，完成后通过 ModHit 的属性通知刷新对应列表项。</summary>
    private static async Task LoadIconsAsync(IEnumerable<ModHit> hits, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(4);
        var tasks = hits.Where(h => h.IconUrl.Length > 0 && h.Icon is null).Select(async hit =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var icon = ImageCache.TryGet(hit.IconUrl) ?? await ImageCache.GetAsync(hit.IconUrl, ct).ConfigureAwait(false);
                if (icon is not null && !ct.IsCancellationRequested)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() => hit.Icon = icon);
                }
            }
            catch (OperationCanceledException)
            {
                // 页面已离开
            }
            catch
            {
                // 单张图标失败不打断整个列表
            }
            finally
            {
                gate.Release();
            }
        }).ToList();
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private void ModList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModList.SelectedItem is not ModHit hit)
        {
            ModTitle.Text = "搜索并选择一个模组";
            ModInfo.Text = "";
            InstallModButton.IsEnabled = false;
            return;
        }
        ModTitle.Text = hit.Title;
        ModInfo.Text = $"{hit.Author}\n{hit.Downloads} 次下载\n分类：{hit.Categories}\n兼容版本：{hit.GameVersions}\n\n{hit.Description}";
        InstallModButton.IsEnabled = TargetList.SelectedItem is InstanceInfo && !_modBusy;
    }

    private void ModList_PreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var container = ItemsControl.ContainerFromElement(ModList, (DependencyObject)e.OriginalSource) as ListBoxItem;
        if (container?.DataContext is ModHit hit)
        {
            ModList.SelectedItem = hit;
            _ = ShowDetailAsync(hit);
        }
    }

    private void OpenDetail_Click(object sender, RoutedEventArgs e)
    {
        if (ModList.SelectedItem is ModHit hit)
        {
            _ = ShowDetailAsync(hit);
        }
        else
        {
            AppDialog.Alert("请先在列表里选择一个模组。", "模组详情");
        }
    }

    /// <summary>应用内详情页：图标、截图、完整介绍，全部在这里渲染，不打开浏览器。</summary>
    private async Task ShowDetailAsync(ModHit hit)
    {
        _detailCts?.Cancel();
        _detailCts = new CancellationTokenSource();
        var token = _detailCts.Token;

        _detail = null;
        ModDetailPanel.Visibility = Visibility.Visible;
        DetailTitleText.Text = hit.Title;
        DetailDescText.Text = hit.Description;
        DetailMetaText.Text = $"{hit.Author} · {hit.Downloads} 次下载";
        DetailCategoryText.Text = hit.Categories.Length > 0 ? hit.Categories : "模组";
        DetailIcon.Source = hit.Icon;
        GalleryList.ItemsSource = null;
        DetailDocumentBody.Document = new FlowDocument(new Paragraph(new Run("正在加载模组介绍…")));
        DetailBar.Value = 0;
        DetailStatus.Text = "正在读取详情…";
        DetailTargetBox.SelectedItem = TargetList.SelectedItem;
        DetailInstallButton.IsEnabled = TargetList.SelectedItem is InstanceInfo && !_modBusy;
        SourceButton.IsEnabled = hit.Slug.Length > 0;
        IssuesButton.IsEnabled = false;
        GalleryLabel.Visibility = Visibility.Collapsed;
        GalleryList.Visibility = Visibility.Collapsed;

        if (hit.Icon is null && hit.IconUrl.Length > 0)
        {
            var icon = await ImageCache.GetAsync(hit.IconUrl, token);
            if (icon is not null && !token.IsCancellationRequested)
            {
                DetailIcon.Source = icon;
            }
        }

        try
        {
            var detail = await Modrinth.GetProjectAsync(hit.ProjectId, token);
            token.ThrowIfCancellationRequested();
            _detail = detail;

            DetailTitleText.Text = detail.Title.Length > 0 ? detail.Title : hit.Title;
            DetailDescText.Text = detail.Description.Length > 0 ? detail.Description : hit.Description;
            DetailMetaText.Text =
                $"{hit.Author} · {detail.Downloads} 次下载 · {detail.Followers} 关注\n" +
                $"支持：{string.Join(" / ", detail.Loaders)}\n" +
                $"适用版本：{string.Join(" ", detail.GameVersions.TakeLast(6))}";
            DetailCategoryText.Text = detail.Categories.Count > 0 ? string.Join(" / ", detail.Categories) : "模组";
            SourceButton.IsEnabled = detail.SourceUrl.Length > 0;
            IssuesButton.IsEnabled = detail.IssuesUrl.Length > 0;

            var brushText = (Brush)FindResource("TextBrush");
            var brushSub = (Brush)FindResource("SubTextBrush");
            var accent = (Color)FindResource("AccentColor");
            DetailDocumentBody.Document = MarkdownDoc.Build(detail.Body, brushText, brushSub, accent);

            // 记录原文，等翻译完成后可以来回切换
            _bodyOriginal = detail.Body;
            _bodyTranslated = null;
            _descOriginal = detail.Description;
            _descTranslated = null;
            _showingOriginal = false;
            BodyToggleButton.Visibility = Visibility.Collapsed;

            if (App.Core.Config.TranslateMods && Translator.NeedsTranslation(detail.Body))
            {
                _ = TranslateBodyAsync(detail, token, brushText, brushSub, accent);
            }

            _ = LoadGalleryAsync(detail.Gallery, token);

            // 顺便告诉用户会装哪个版本
            var target = TargetList.SelectedItem as InstanceInfo;
            if (target is not null)
            {
                var versions = await Modrinth.GetVersionsAsync(hit.ProjectId, target.Loader, BaseGameVersion(target), token);
                DetailStatus.Text = versions.Count > 0
                    ? $"将安装：{versions[0].Name}（{target.LoaderLabel} / {BaseGameVersion(target)}）"
                    : "当前实例没有可用版本，换一个实例或取消兼容过滤。";
            }
            else
            {
                DetailStatus.Text = "选择右侧实例后即可安装。";
            }
        }
        catch (OperationCanceledException)
        {
            // 已切到别的模组
        }
        catch (Exception ex)
        {
            DetailStatus.Text = "详情加载失败：" + ex.Message;
            DetailDocumentBody.Document = new FlowDocument(new Paragraph(new Run("详情加载失败：" + ex.Message)));
        }
    }

    /// <summary>后台把英文介绍翻成中文，成功后替换正文并露出「查看原文」按钮。</summary>
    private async Task TranslateBodyAsync(ModDetail detail, CancellationToken ct, Brush brushText, Brush brushSub, Color accent)
    {
        try
        {
            App.Core.LogLine($"[翻译] 开始翻译 {detail.Title} 的介绍（{detail.Body.Length} 字符）");
            TraceTranslate($"start {detail.Title} {detail.Body.Length}");
            var zhBody = await Translator.TranslateAsync(detail.Body, ct);
            var zhDesc = detail.Description.Length > 0
                ? await Translator.TranslateAsync(detail.Description, ct)
                : "";
            if (ct.IsCancellationRequested)
            {
                return;
            }
            _bodyTranslated = zhBody;
            _descTranslated = zhDesc;
            DetailDocumentBody.Document = MarkdownDoc.Build(zhBody, brushText, brushSub, accent);
            if (zhDesc.Length > 0)
            {
                DetailDescText.Text = zhDesc;
            }
            BodyToggleButton.Content = "查看原文";
            BodyToggleButton.Visibility = Visibility.Visible;
            App.Core.LogLine($"[翻译] 完成，译文 {zhBody.Length} 字符");
            TraceTranslate($"done {zhBody.Length}");
        }
        catch (OperationCanceledException)
        {
            // 用户切换了模组，丢弃结果
        }
        catch
        {
            // 翻译失败就继续显示英文原文
        }
    }

    /// <summary>临时的翻译排查日志（写临时目录，便于无界面自动核对）。</summary>
    private static void TraceTranslate(string line)
    {
        try
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "echo_translate.log"),
                $"{DateTime.Now:HH:mm:ss} {line}\n");
        }
        catch
        {
            // 排查日志失败不影响功能
        }
    }

    /// <summary>
    /// FlowDocumentScrollViewer 自带滚动容器，会把滚轮事件吃掉且自身已禁用滚动条，
    /// 导致用户把鼠标放在正文中间滚不动、只能去右侧滚动条上拖。这里把它转发给外层 ScrollViewer。
    /// </summary>
    private void Body_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }
        DependencyObject? node = sender as DependencyObject;
        while (node is not null && node is not ScrollViewer)
        {
            node = VisualTreeHelper.GetParent(node);
        }
        if (node is ScrollViewer outer)
        {
            // 每格滚轮 120，按 48px/格 滚动，和系统默认手感接近
            outer.ScrollToVerticalOffset(outer.VerticalOffset - e.Delta / 120.0 * 48);
            e.Handled = true;
        }
    }

    private void BodyToggle_Click(object sender, RoutedEventArgs e)
    {
        var brushText = (Brush)FindResource("TextBrush");
        var brushSub = (Brush)FindResource("SubTextBrush");
        var accent = (Color)FindResource("AccentColor");
        _showingOriginal = !_showingOriginal;
        var text = _showingOriginal ? _bodyOriginal : _bodyTranslated ?? _bodyOriginal;
        DetailDocumentBody.Document = MarkdownDoc.Build(text, brushText, brushSub, accent);
        DetailDescText.Text = _showingOriginal
            ? _descOriginal
            : _descTranslated ?? _descOriginal;
        BodyToggleButton.Content = _showingOriginal ? "查看中文" : "查看原文";
    }

    private async Task LoadGalleryAsync(List<string> urls, CancellationToken ct)
    {
        var loaded = new List<ImageSource>();
        foreach (var url in urls.Take(6))
        {
            var image = await ImageCache.GetAsync(url, ct);
            if (image is not null && !ct.IsCancellationRequested)
            {
                loaded.Add(image);
                GalleryList.ItemsSource = null;
                GalleryList.ItemsSource = loaded.ToList();
                GalleryLabel.Visibility = Visibility.Visible;
                GalleryList.Visibility = Visibility.Visible;
            }
        }
        if (loaded.Count == 0)
        {
            GalleryLabel.Visibility = Visibility.Collapsed;
            GalleryList.Visibility = Visibility.Collapsed;
        }
    }

    private void BackToList_Click(object sender, RoutedEventArgs e)
    {
        _detailCts?.Cancel();
        ModDetailPanel.Visibility = Visibility.Collapsed;
    }

    private void OpenSource_Click(object sender, RoutedEventArgs e)
    {
        if (_detail is not null && _detail.SourceUrl.Length > 0)
        {
            Process.Start(new ProcessStartInfo(_detail.SourceUrl) { UseShellExecute = true });
        }
    }

    private void OpenIssues_Click(object sender, RoutedEventArgs e)
    {
        if (_detail is not null && _detail.IssuesUrl.Length > 0)
        {
            Process.Start(new ProcessStartInfo(_detail.IssuesUrl) { UseShellExecute = true });
        }
    }

    private async void InstallMod_Click(object sender, RoutedEventArgs e)
    {
        if (_modBusy)
        {
            return;
        }
        if (ModList.SelectedItem is not ModHit hit || TargetList.SelectedItem is not InstanceInfo instance)
        {
            return;
        }

        _modBusy = true;
        InstallModButton.IsEnabled = false;
        DetailInstallButton.IsEnabled = false;
        _modCts?.Cancel();
        _modCts = new CancellationTokenSource();
        var token = _modCts.Token;
        var progress = new Progress<InstallProgress>(p =>
        {
            ModBar.Value = p.Fraction * 100;
            DetailBar.Value = p.Fraction * 100;
            ModStatus.Text = $"{p.Stage} · {p.Detail}";
            if (ModDetailPanel.Visibility == Visibility.Visible)
            {
                DetailStatus.Text = $"{p.Stage} · {p.Detail}";
            }
        });

        try
        {
            var game = BaseGameVersion(instance);
            ModStatus.Text = $"正在为 {instance.Name}（{game} / {instance.LoaderLabel}）安装…";
            var files = await Modrinth.InstallAsync(hit.ProjectId, instance.Directory, instance.Loader, game, progress, token);
            ModBar.Value = 100;
            DetailBar.Value = 100;
            var message = $"已安装 {files.Count} 个文件到 mods：{string.Join("、", files)}";
            ModStatus.Text = message;
            DetailStatus.Text = message;
        }
        catch (OperationCanceledException)
        {
            ModStatus.Text = "已取消";
            DetailStatus.Text = "已取消";
        }
        catch (LauncherException ex)
        {
            ModStatus.Text = ex.Message;
            DetailStatus.Text = ex.Message;
            AppDialog.Alert(ex.Message, "安装模组", true);
        }
        catch (Exception ex)
        {
            ModStatus.Text = "安装失败：" + ex.Message;
            DetailStatus.Text = "安装失败：" + ex.Message;
            AppDialog.Alert(ex.Message, "安装模组", true);
        }
        finally
        {
            _modBusy = false;
            InstallModButton.IsEnabled = ModList.SelectedItem is ModHit;
            DetailInstallButton.IsEnabled = _detail is not null && TargetList.SelectedItem is InstanceInfo;
        }
    }

    private void OpenMods_Click(object sender, RoutedEventArgs e)
    {
        var instance = TargetList.SelectedItem as InstanceInfo
                       ?? App.Core.Instances.FirstOrDefault(i => i.Loader.Length > 0);
        if (instance is null)
        {
            AppDialog.Alert("还没有可用的模组实例。", "模组");
            return;
        }
        var mods = Path.Combine(instance.Directory, "mods");
        try
        {
            Directory.CreateDirectory(mods);
            Process.Start(new ProcessStartInfo(mods) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppDialog.Alert(ex.Message, "打开 mods 文件夹", true);
        }
    }
}


