namespace FanKou.Ui.Rendering;

/// <summary>配色和尺寸。想换风格改这里一处就行。</summary>
public sealed class Theme
{
    public static Theme Default { get; } = new();

    // 桌面
    public Color TableInner { get; init; } = Color.FromArgb(24, 96, 66);
    public Color TableOuter { get; init; } = Color.FromArgb(12, 56, 39);
    public Color TableLine { get; init; } = Color.FromArgb(70, 150, 110);

    // 牌面
    public Color CardFace { get; init; } = Color.FromArgb(252, 252, 250);
    public Color CardBorder { get; init; } = Color.FromArgb(120, 120, 120);
    public Color CardShadow { get; init; } = Color.FromArgb(60, 0, 0, 0);
    public Color RedSuit { get; init; } = Color.FromArgb(200, 42, 42);
    public Color BlackSuit { get; init; } = Color.FromArgb(32, 32, 36);

    // 牌背
    public Color BackBase { get; init; } = Color.FromArgb(40, 76, 148);
    public Color BackAccent { get; init; } = Color.FromArgb(226, 233, 248);
    public Color BackBorder { get; init; } = Color.FromArgb(24, 48, 96);

    // 交互
    public Color SelectedGlow { get; init; } = Color.FromArgb(255, 214, 102);
    public Color HoverGlow { get; init; } = Color.FromArgb(190, 255, 235, 160);
    public Color HintGlow { get; init; } = Color.FromArgb(150, 140, 220, 255);

    /// <summary>标记用的蓝边，比如"这张是从锅底拿上来的"。</summary>
    public Color MarkedGlow { get; init; } = Color.FromArgb(120, 200, 255);

    // 文字
    public Color TextPrimary { get; init; } = Color.FromArgb(240, 245, 242);
    public Color TextMuted { get; init; } = Color.FromArgb(176, 200, 188);
    public Color TextWarning { get; init; } = Color.FromArgb(255, 196, 120);
    public Color TextDanger { get; init; } = Color.FromArgb(255, 150, 140);
    public Color Accent { get; init; } = Color.FromArgb(255, 214, 102);

    // 尺寸（桌面上的牌按这个比例缩放）
    public float CardAspect { get; init; } = 0.70f;   // 宽 / 高

    public int CornerRadius(int height) => Math.Max(3, height / 14);

    /// <summary>牌背的菱形花纹斜线间距。</summary>
    public int BackStripeStep => 9;
}
