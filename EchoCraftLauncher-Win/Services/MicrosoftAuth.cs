using System.Text.Json;

namespace SwiftCraftLauncher2.Services;

/// <summary>微软账号（Minecraft 正版）OAuth 登录链路：MSA → Xbox Live → XSTS → Minecraft Services。</summary>
public static class MicrosoftAuth
{
    // 微软官方公开的「Minecraft 启动器」OAuth 客户端 ID
    public const string ClientId = "00000000402b5328";
    public const string Scope = "XboxLive.signin offline_access";
    public const string RedirectUri = "https://login.live.com/oauth20_desktop.srf";

    private const string AuthorizeEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize";
    private const string TokenEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
    private const string XblEndpoint = "https://user.auth.xboxlive.com/user/authenticate";
    private const string XstsEndpoint = "https://xsts.auth.xboxlive.com/xsts/authorize";
    private const string McLoginEndpoint = "https://api.minecraftservices.com/authentication/login_with_xbox";
    private const string McProfileEndpoint = "https://api.minecraftservices.com/minecraft/profile";

    public static string BuildAuthorizeUrl(string state) =>
        AuthorizeEndpoint
        + "?client_id=" + Uri.EscapeDataString(ClientId)
        + "&response_type=code"
        + "&scope=" + Uri.EscapeDataString(Scope)
        + "&redirect_uri=" + Uri.EscapeDataString(RedirectUri)
        + "&prompt=select_account"
        + "&state=" + Uri.EscapeDataString(state);

    /// <summary>从跳转回来的地址中取出 code；不是回调地址时返回 null。</summary>
    public static string? ExtractCode(string? url)
    {
        if (string.IsNullOrEmpty(url) || !url.Contains("oauth20_desktop.srf", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var queryIndex = url.IndexOf('?');
        if (queryIndex < 0)
        {
            return null;
        }
        foreach (var pair in url[(queryIndex + 1)..].Split('&'))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0] == "code")
            {
                return Uri.UnescapeDataString(parts[1]);
            }
            if (parts.Length == 2 && parts[0] == "error")
            {
                throw new LauncherException($"微软登录被取消或失败：{Uri.UnescapeDataString(parts[1])}");
            }
        }
        return null;
    }

    /// <summary>用授权码换取完整账号信息。</summary>
    public static async Task<AccountInfo> CompleteAsync(string code, CancellationToken ct)
    {
        var token = await Net.PostFormAsync(TokenEndpoint, new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = RedirectUri,
            ["scope"] = Scope
        }, ct).ConfigureAwait(false);

        using (token)
        {
            var accessToken = ReadString(token.RootElement, "access_token");
            var refreshToken = ReadString(token.RootElement, "refresh_token");
            if (accessToken.Length == 0)
            {
                throw new LauncherException("微软未返回 access_token，登录失败");
            }
            return await ChainAsync(accessToken, refreshToken, ct).ConfigureAwait(false);
        }
    }

    /// <summary>使用 refresh_token 静默续期。</summary>
    public static async Task<AccountInfo> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var token = await Net.PostFormAsync(TokenEndpoint, new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
            ["redirect_uri"] = RedirectUri,
            ["scope"] = Scope
        }, ct).ConfigureAwait(false);

        using (token)
        {
            var accessToken = ReadString(token.RootElement, "access_token");
            var newRefresh = ReadString(token.RootElement, "refresh_token");
            if (newRefresh.Length == 0)
            {
                newRefresh = refreshToken;
            }
            if (accessToken.Length == 0)
            {
                throw new LauncherException("微软令牌续期失败，请重新登录");
            }
            return await ChainAsync(accessToken, newRefresh, ct).ConfigureAwait(false);
        }
    }

    private static async Task<AccountInfo> ChainAsync(string msaAccessToken, string refreshToken, CancellationToken ct)
    {
        // 1. Xbox Live 用户认证
        using var xbl = await Net.PostJsonAsync(XblEndpoint, new
        {
            Properties = new { AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com", RpsTicket = "d=" + msaAccessToken },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        }, ct).ConfigureAwait(false);

        var xblToken = ReadString(xbl.RootElement, "Token");
        var userHash = "";
        if (xbl.RootElement.TryGetProperty("DisplayClaims", out var claims)
            && claims.TryGetProperty("xui", out var xui)
            && xui.ValueKind == JsonValueKind.Array
            && xui.GetArrayLength() > 0)
        {
            userHash = ReadString(xui[0], "uhs");
        }
        if (xblToken.Length == 0 || userHash.Length == 0)
        {
            throw new LauncherException("Xbox Live 认证失败");
        }

        // 2. XSTS 授权
        using var xsts = await Net.PostJsonAsync(XstsEndpoint, new
        {
            Properties = new { SandboxId = "RETAIL", UserTokens = new[] { xblToken } },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT"
        }, ct).ConfigureAwait(false);

        var xstsToken = ReadString(xsts.RootElement, "Token");
        var xuid = "";
        if (xsts.RootElement.TryGetProperty("DisplayClaims", out var xstsClaims)
            && xstsClaims.TryGetProperty("xui", out var xstsXui)
            && xstsXui.ValueKind == JsonValueKind.Array
            && xstsXui.GetArrayLength() > 0)
        {
            xuid = ReadString(xstsXui[0], "xid");
            userHash = ReadString(xstsXui[0], "uhs");
        }

        // 3. 换取 Minecraft 令牌
        using var login = await Net.PostJsonAsync(McLoginEndpoint, new
        {
            identityToken = $"XBL3.0 x={userHash};{xstsToken}"
        }, ct).ConfigureAwait(false);

        var mcToken = ReadString(login.RootElement, "access_token");
        if (mcToken.Length == 0)
        {
            throw new LauncherException("Minecraft 登录失败：未获得游戏令牌");
        }

        // 4. 校验是否拥有游戏
        using var profile = await Net.GetJsonAsync(McProfileEndpoint, ct).ConfigureAwait(false);
        var name = ReadString(profile.RootElement, "name");
        var uuid = ReadString(profile.RootElement, "id");
        if (name.Length == 0)
        {
            throw new LauncherException(
                "该微软账号没有已购买的 Minecraft: Java Edition。\n\n" +
                "请前往 https://www.minecraft.net/zh-hans/store/minecraft-java-edition 购买正版后重试；\n" +
                "也可以先切换到「离线登录」体验游戏。");
        }

        var expiresIn = login.RootElement.TryGetProperty("expires_in", out var expiresElement)
            && expiresElement.TryGetInt64(out var seconds) && seconds > 0
                ? seconds
                : 82800; // 接口未返回时按 23 小时

        return new AccountInfo
        {
            Type = "msa",
            Name = name,
            Uuid = uuid,
            AccessToken = mcToken,
            RefreshToken = refreshToken,
            Xuid = xuid,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(expiresIn - 60, 300)).ToUnixTimeSeconds()
        };
    }

    private static string ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}
