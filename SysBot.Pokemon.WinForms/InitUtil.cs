using PKHeX.Core;
using PKHeX.Drawing.PokeSprite;

namespace SysBot.Pokemon.WinForms;

public static class InitUtil
{
    public static void InitializeStubs(GameMode mode, string trainer, LanguageID language)
    {
        Trainer = trainer;
        Language = language;
        var sav = GetFakeSaveFile(mode);
        SetUpSpriteCreator(sav);
    }

    private static string Trainer { get; set; } = "SysBot";
    private static LanguageID Language { get; set; } = LanguageID.English;
    private static SaveFile Get(GameVersion version) => BlankSaveFile.Get(version, Trainer, Language);

    private static SaveFile GetFakeSaveFile(GameMode mode) => mode switch
    {
        GameMode.SWSH => Get(GameVersion.SW),
        GameMode.BDSP => Get(GameVersion.BD),
        GameMode.LA   => Get(GameVersion.PLA),
        GameMode.SV   => Get(GameVersion.SL),
        GameMode.LZA  => Get(GameVersion.ZA),
        _                => throw new System.ArgumentOutOfRangeException(nameof(mode)),
    };

    private static void SetUpSpriteCreator(SaveFile sav)
    {
        SpriteUtil.Initialize(sav);
        StreamSettings.CreateSpriteFile = (pk, fn) =>
        {
            var png = pk.Sprite();
            png.Save(fn);
        };
    }
}
