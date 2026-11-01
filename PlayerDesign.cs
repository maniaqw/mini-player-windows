namespace GlassPlayer;

public enum PlayerDesign { Classic, LiquidGlass }

public static class PlayerDesigns
{
    public static PlayerDesign Parse(string? value) => value == nameof(PlayerDesign.LiquidGlass) ? PlayerDesign.LiquidGlass : PlayerDesign.Classic;
}
