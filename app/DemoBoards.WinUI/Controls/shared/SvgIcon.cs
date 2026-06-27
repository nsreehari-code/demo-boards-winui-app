using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace DemoBoards_WinUI.Controls.Shared;

/// <summary>
/// Shared decorative icon: renders one of the host SVG assets (see <see cref="Assets.HostIconSources"/>)
/// at a square size. The web renders Bootstrap icon glyphs via a font; WinUI has no icon font, so this is
/// the single host-side seam that turns a named icon source into an image. Always accessibility-hidden —
/// the meaningful label belongs on the interactive parent (button), never on the glyph.
/// </summary>
public sealed record SvgIconProps(
    string Icon,
    double Size = 18);

public sealed class SvgIcon : Component<SvgIconProps>
{
    public override Element Render() =>
        Image(Props.Icon)
            .Width(Props.Size)
            .Height(Props.Size)
            .AccessibilityHidden();
}
