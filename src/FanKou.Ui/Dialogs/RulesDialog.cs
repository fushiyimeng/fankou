using FanKou.Ui.Rendering;

namespace FanKou.Ui.Dialogs;

/// <summary>
/// 「帮助 → 规则说明」。**先给简略版**，想细看就点下面的按钮翻到详细版。
///
/// 详细版的内容**不是写在这里的**，是项目根目录那份《玩法说明.md》——
/// 构建时整个嵌进程序集，运行时读出来显示。这样文档只有一份，不会出现
/// 改了 md 忘了改程序、两边说法打架的情况（这个项目已经因为文档前后不一致
/// 栽过一次了，见《规则实现笔记》里的第 69 行 / 第 102 行）。
/// </summary>
public sealed class RulesDialog : Form
{
    /// <summary>
    /// 简略版。原文里的换行是源文件的换行（LF），**必须过一遍 <see cref="Normalize"/>**——
    /// WinForms 的 TextBox 只认 \r\n，单独一个 \n 不当换行，整段会挤成一坨（踩过）。
    /// </summary>
    private static readonly string BriefText = Normalize("""
        双扣（方扣）· 4 人两副牌 108 张。对家一伙：东西一队、南北一队。

        一局：叫主 → 换底牌 → 出牌 25 墩 → 算分定升降。

        · 主牌 = 主花色全部 + 大小王 + 所有花色的级牌
        · 副级牌算主牌，不算它原来的花色（主黑桃打 5 时 ♣5 是主，出梅花不用给）
        · 有该花色必须给该花色；跟对子/拖拉机时这个花色一张都没有，就得垫主牌
        · 闲家捡分、庄家扣分。5 是 5 分，10 和 K 各 10 分，整副共 200 分
        · 闲家 120 分升一级、200 分升两级；85~114 分触发下一局抢主
        · 底牌只有闲家赢下最后一手才计分，倍率看最后一手：单张 ×2、对子 ×4、连对 ×8
        · 出牌逆时针（东→北→西→南），发牌顺时针，两个方向是反的
        · 顶牌 AAK / AAQ 算甩牌，必然合法

        详细玩法（含打法思路）点下面的按钮。
        """);

    private readonly TextBox _body;
    private readonly Button _toggle;
    private readonly string _detailText;

    private bool _showingDetail;

    private RulesDialog(CardRenderer renderer, string detailText)
    {
        _detailText = detailText;

        Text = "规则说明";
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ClientSize = new Size(760, 620);
        MinimumSize = new Size(520, 400);
        BackColor = renderer.Theme.TableOuter;
        ForeColor = renderer.Theme.TextPrimary;

        _body = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            BackColor = renderer.Theme.TableInner,
            ForeColor = renderer.Theme.TextPrimary,
            Font = new Font("Microsoft YaHei", 13.5f, FontStyle.Regular, GraphicsUnit.Pixel),
            TabStop = false,
            Text = BriefText,
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 56,
            Padding = new Padding(10),
            BackColor = renderer.Theme.TableOuter,
        };

        _toggle = new Button
        {
            Text = "看详细的玩法介绍",
            Width = 190,
            Height = 38,
            FlatStyle = FlatStyle.Flat,
        };
        _toggle.Click += (_, _) => Toggle();

        var close = new Button
        {
            Text = "关闭",
            Width = 110,
            Height = 38,
            FlatStyle = FlatStyle.Flat,
        };
        close.Click += (_, _) => Close();

        buttons.Controls.Add(close);
        buttons.Controls.Add(_toggle);

        // 正文外面留一圈边距，别贴着窗口边。
        var padding = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            BackColor = renderer.Theme.TableOuter,
        };
        padding.Controls.Add(_body);

        Controls.Add(padding);
        Controls.Add(buttons);
    }

    public static void Show(IWin32Window owner, CardRenderer renderer) =>
        Show(owner, renderer, LoadDetailText());

    public static void Show(IWin32Window owner, CardRenderer renderer, string detailText)
    {
        using var dialog = new RulesDialog(renderer, detailText);
        dialog.ShowDialog(owner);
    }

    /// <summary>
    /// 开发用：把对话框建出来渲染成图片检查排版（跟 <c>BottomSwapDialog.CreateForPreview</c> 一个路子）。
    /// 顺便也验一遍嵌入的《玩法说明.md》到底读不读得到。
    /// </summary>
    public static RulesDialog CreateForPreview(CardRenderer renderer, bool detail = false)
    {
        var dialog = new RulesDialog(renderer, LoadDetailText());

        if (detail)
        {
            dialog.Toggle();
        }

        return dialog;
    }

    /// <summary>
    /// 把《玩法说明.md》读出来转成纯文本。取不到就退化成一句提示——
    /// 文档丢了不该让"规则说明"这个菜单打不开。
    /// </summary>
    private static string LoadDetailText()
    {
        using var stream = typeof(RulesDialog).Assembly
            .GetManifestResourceStream("FanKou.Ui.rules-full.md");

        if (stream is null)
        {
            return "（详细玩法说明没打进程序里，请看项目目录下的《玩法说明.md》。）";
        }

        using var reader = new StreamReader(stream);
        return ToPlainText(reader.ReadToEnd());
    }

    /// <summary>统一成 TextBox 认的换行。LF 单独一个它不认，会整段挤在一起。</summary>
    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n").Replace('\n', '\r').Replace("\r", Environment.NewLine);

    /// <summary>
    /// 把 markdown 的记号去掉：标题的 #、粗体的 **、水平线。
    /// 不做真渲染——这里是纯文本框，能读顺就行。
    /// </summary>
    private static string ToPlainText(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();

            if (line.Trim() == "---")
            {
                lines[i] = string.Empty;
                continue;
            }

            lines[i] = line.TrimStart('#').TrimStart().Replace("**", string.Empty);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void Toggle()
    {
        _showingDetail = !_showingDetail;

        _body.Text = _showingDetail ? _detailText : BriefText;
        _toggle.Text = _showingDetail ? "返回简略版" : "看详细的玩法介绍";

        // 换了正文之后滚回顶部，不然会停在上一次的滚动位置。
        _body.SelectionStart = 0;
        _body.SelectionLength = 0;
        _body.ScrollToCaret();
    }
}
