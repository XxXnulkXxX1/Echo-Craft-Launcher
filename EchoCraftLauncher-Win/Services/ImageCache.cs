using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SwiftCraftLauncher2.Services;

/// <summary>
/// 网络图片缓存：按 URL 只下载一次，解码后 Freeze 便于跨线程共享。
/// 列表虚拟化会反复创建元素，没有缓存会重复请求同一张图标。
/// </summary>
public static class ImageCache
{
    private static readonly ConcurrentDictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? TryGet(string url) =>
        url.Length > 0 && Cache.TryGetValue(url, out var image) ? image : null;

    public static async Task<ImageSource?> GetAsync(string url, CancellationToken ct = default)
    {
        if (url.Length == 0)
        {
            return null;
        }
        if (Cache.TryGetValue(url, out var cached))
        {
            return cached;
        }
        try
        {
            var bytes = await Net.GetBytesAsync(url, ct).ConfigureAwait(false);
            if (bytes.Length == 0)
            {
                return null;
            }
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            // 不要设置 IgnoreImageCache：配合内存流时内部会拿空 URI 去清缓存并抛异常
            // 图标与截图都按显示尺寸解码，避免把 4K 截图整张读进内存
            bitmap.DecodePixelWidth = 320;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            Cache[url] = bitmap;
            return bitmap;
        }
        catch (Exception ex)
        {
            // 图片加载失败不影响列表可用性，显示占位色块即可
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "echo_image.log"),
                    $"{url} -> {ex}\n");
            }
            catch
            {
                // 忽略日志失败
            }
            return null;
        }
    }
}

