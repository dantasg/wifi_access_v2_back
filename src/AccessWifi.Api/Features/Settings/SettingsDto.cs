using Models.DataBase;

namespace AccessWifi.Api.Features.Settings;

public record SettingsDto(
    ThemeColorsDto Colors,
    string? Logo,
    string? Favicon,
    string? Banner,
    string Ssid,
    int AccessMinutes,
    string? RedirectUrl,
    // Slug da unidade resolvida. Só sai na leitura do portal (o front precisa dele para o
    // /authorize quando a unidade veio pelo host); ignorado no PUT do painel.
    string? Unit = null)
{
    public static SettingsDto FromEntity(PortalSettings objSettings, string? sUnitSlug = null)
    {
        return new SettingsDto(
            Colors: ThemeColorsDto.FromEntity(objSettings.Colors),
            Logo: objSettings.Logo,
            Favicon: objSettings.Favicon,
            Banner: objSettings.Banner,
            Ssid: objSettings.Ssid,
            AccessMinutes: objSettings.AccessMinutes,
            RedirectUrl: objSettings.RedirectUrl,
            Unit: sUnitSlug);
    }

    /// <summary>
    /// Leitura do portal: as imagens vão como endereço (<see cref="PortalImage"/>), não como dados. A
    /// resposta cai de ~100 KB para ~1 KB, e o tema chega antes ao celular do visitante.
    /// </summary>
    public static SettingsDto ForPortal(PortalSettings objSettings, string sUnitSlug)
    {
        return FromEntity(objSettings, sUnitSlug) with
        {
            Logo = PortalImage.Url(sUnitSlug, PortalImage.Logo, objSettings.Logo),
            Favicon = PortalImage.Url(sUnitSlug, PortalImage.Favicon, objSettings.Favicon),
            Banner = PortalImage.Url(sUnitSlug, PortalImage.Banner, objSettings.Banner),
        };
    }
}

public record ThemeColorsDto(
    string Brand,
    string BrandDark,
    string Surface,
    string Card,
    string Field,
    string Ink,
    string Muted,
    string Line)
{
    public static ThemeColorsDto FromEntity(ThemeColors objColors)
    {
        return new ThemeColorsDto(
            objColors.Brand, objColors.BrandDark, objColors.Surface, objColors.Card,
            objColors.Field, objColors.Ink, objColors.Muted, objColors.Line);
    }

    public ThemeColors ToEntity()
    {
        return new ThemeColors
        {
            Brand = Brand,
            BrandDark = BrandDark,
            Surface = Surface,
            Card = Card,
            Field = Field,
            Ink = Ink,
            Muted = Muted,
            Line = Line,
        };
    }
}
