using Models.DataBase;

namespace AccessWifi.Api.Features.Settings;

public record SettingsDto(
    ThemeColorsDto Colors,
    string? Logo,
    string? Favicon,
    string? Banner,
    string Ssid,
    int AccessMinutes)
{
    public static SettingsDto FromEntity(PortalSettings objSettings)
    {
        return new SettingsDto(
            Colors: ThemeColorsDto.FromEntity(objSettings.Colors),
            Logo: objSettings.Logo,
            Favicon: objSettings.Favicon,
            Banner: objSettings.Banner,
            Ssid: objSettings.Ssid,
            AccessMinutes: objSettings.AccessMinutes);
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
