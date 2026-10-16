using FanKou.Core.Cards;
using FanKou.Ui.Rendering;

namespace FanKou.Ui.Controls;

public sealed class SelectionChangedEventArgs(IReadOnlyList<Card> selected) : EventArgs
{
    public IReadOnlyList<Card> Selected { get; } = selected;
}

/// <summary>
/// 自己那一手牌。用自绘而不是摆 25 个 PictureBox——
/// 牌要重叠、悬停要浮起、点选要上移，用控件拼会卡且 z-order 很难管。
///
/// 画的时候**严格按顺序画**：后面的牌永远盖住前面的。选中的牌只往上抬一点，
/// 不会因为"要显示在最上面"就压住右边的牌——那样会让人看不见后面的牌。
/// </summary>
public sealed class HandPanel : Control
{
    private readonly CardRenderer _renderer;
    private IReadOnlyList<Card> _hand = [];
    private readonly HashSet<int> _selected = [];
    private HashSet<int> _locked = [];
    private HashSet<int> _marked = [];
    private int _hoverIndex = -1;

    private readonly List<Rectangle> _hitBoxes = [];
    private HandLayout? _layout;

    public HandPanel(CardRenderer renderer)
    {
        _renderer = renderer;
        DoubleBuffered = true;
        BackColor = renderer.Theme.TableOuter;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }

    /// <summary>允许玩家点牌。AI 出牌或者还没轮到自己的时候关掉。</summary>
    public bool InteractionEnabled { get; set; }

    private int _rows = 1;

    /// <summary>手牌分几行摆。牌多的时候分行才看得清花色。</summary>
    public int Rows
    {
        get => _rows;
        set
        {
            var clamped = Math.Clamp(value, 1, 4);
            if (clamped == _rows)
            {
                return;
            }

            _rows = clamped;
            Invalidate();
        }
    }

    /// <summary>不许选的牌。换底牌时用来锁住叫主摆出来的明牌（文档第 56 行）。</summary>
    public IReadOnlyCollection<Card> LockedCards
    {
        get => [.. _hand.Where(c => _locked.Contains(c.Id))];
        set
        {
            _locked = [.. value.Select(c => c.Id)];
            _selected.RemoveWhere(id => _locked.Contains(id));
            Invalidate();
        }
    }

    /// <summary>要特别标出来的牌（蓝边）。换底牌时用来标"这几张是从锅底拿上来的"。</summary>
    public IReadOnlyCollection<Card> MarkedCards
    {
        get => [.. _hand.Where(c => _marked.Contains(c.Id))];
        set
        {
            _marked = [.. value.Select(c => c.Id)];
            Invalidate();
        }
    }

    public IReadOnlyList<Card> Hand => _hand;

    public event EventHandler<SelectionChangedEventArgs>? SelectionChanged;

    public IReadOnlyList<Card> SelectedCards => [.. _hand.Where(c => _selected.Contains(c.Id))];

    public void SetHand(IEnumerable<Card> cards)
    {
        _hand = [.. cards];
        _selected.RemoveWhere(id => !_hand.Any(c => c.Id == id));
        _hoverIndex = -1;
        Invalidate();
        RaiseSelectionChanged();
    }

    public void ClearSelection()
    {
        if (_selected.Count == 0)
        {
            return;
        }

        _selected.Clear();
        Invalidate();
        RaiseSelectionChanged();
    }

    /// <summary>把指定的牌设为选中（"提示"按钮用）。</summary>
    public void Select(IEnumerable<Card> cards)
    {
        _selected.Clear();
        foreach (var card in cards)
        {
            _selected.Add(card.Id);
        }

        Invalidate();
        RaiseSelectionChanged();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        _layout = _hand.Count == 0 ? null : ComputeLayout();

        if (_layout is null)
        {
            return;
        }

        // 按顺序画：后面的自然压在前面上面，选中的牌只抬起来，不改绘制次序。
        for (var i = 0; i < _hand.Count; i++)
        {
            DrawCard(g, i, _layout);
        }
    }

    private void DrawCard(Graphics g, int index, HandLayout layout)
    {
        var card = _hand[index];
        var box = _hitBoxes[index];
        var isLocked = _locked.Contains(card.Id);
        var isSelected = _selected.Contains(card.Id);

        var offset = 0;
        if (isLocked)
        {
            offset -= layout.RaiseOffset / 3;
        }
        else if (isSelected)
        {
            offset -= layout.RaiseOffset;
        }
        else if (index == _hoverIndex && InteractionEnabled)
        {
            offset -= layout.RaiseOffset / 2;
        }

        var target = new Rectangle(box.X, box.Y + offset, box.Width, box.Height);
        g.DrawImage(_renderer.GetCard(card, layout.CardWidth, layout.CardHeight), target);

        // 标记（从锅底拿上来的）——画在最里层
        if (_marked.Contains(card.Id))
        {
            DrawBorder(g, target, _renderer.Theme.MarkedGlow, 2f);
        }

        if (isLocked)
        {
            // 明牌：金边 + 一把小锁，表示这张不能扣。
            DrawBorder(g, target, _renderer.Theme.Accent, 2.5f);

            using var lockFont = new Font(
                "Segoe UI Emoji", Math.Max(9f, target.Height * 0.13f), FontStyle.Regular, GraphicsUnit.Pixel);
            using var lockBrush = new SolidBrush(_renderer.Theme.Accent);
            g.DrawString("🔒", lockFont, lockBrush, target.X + (target.Width * 0.5f) - (target.Width * 0.11f), target.Y + 1);
        }
        else if (isSelected)
        {
            DrawBorder(g, target, _renderer.Theme.SelectedGlow, 3f);
        }
        else if (index == _hoverIndex && InteractionEnabled)
        {
            DrawBorder(g, target, _renderer.Theme.HoverGlow, 2f);
        }
    }

    private void DrawBorder(Graphics g, Rectangle target, Color color, float width)
    {
        using var pen = new Pen(color, width);
        using var path = CardVisual.RoundedRect(
            new Rectangle(target.X + 1, target.Y + 1, target.Width - 2, target.Height - 2),
            _renderer.Theme.CornerRadius(target.Height));
        g.DrawPath(pen, path);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (!InteractionEnabled || e.Button != MouseButtons.Left)
        {
            return;
        }

        var index = HitTest(e.Location);
        if (index < 0)
        {
            return;
        }

        var id = _hand[index].Id;
        if (_locked.Contains(id))
        {
            return;
        }

        if (!_selected.Remove(id))
        {
            _selected.Add(id);
        }

        Invalidate();
        RaiseSelectionChanged();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var index = InteractionEnabled ? HitTest(e.Location) : -1;
        if (index != _hoverIndex)
        {
            _hoverIndex = index;
            Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);

        if (_hoverIndex != -1)
        {
            _hoverIndex = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    /// <summary>从右往左找，压在上面的那张先命中。</summary>
    private int HitTest(Point point)
    {
        if (_layout is null)
        {
            return -1;
        }

        for (var i = _hitBoxes.Count - 1; i >= 0; i--)
        {
            var box = _hitBoxes[i];
            var lifted = _selected.Contains(_hand[i].Id)
                ? new Rectangle(box.X, box.Y - _layout.RaiseOffset, box.Width, box.Height + _layout.RaiseOffset)
                : box;

            if (lifted.Contains(point))
            {
                return i;
            }
        }

        return -1;
    }

    private void RaiseSelectionChanged() =>
        SelectionChanged?.Invoke(this, new SelectionChangedEventArgs(SelectedCards));

    private HandLayout ComputeLayout()
    {
        var rows = Rows;
        var count = _hand.Count;
        var perRow = Math.Max(1, (int)Math.Ceiling(count / (double)rows));

        // 行距要够大，不然下面一行选中的牌往上一浮就会压住上一行的牌脚。
        var rowGap = rows > 1 ? 20 : 0;
        var usable = Math.Max(40, Height - 14 - (rowGap * (rows - 1)));
        var cardHeight = Math.Max(28, usable / rows);

        // 选中的牌要往上浮。第一行头顶必须留出这段高度，否则浮起来的那一截会被切掉——
        // 而牌的左上角正好是点数，一被切就认不出这是什么牌了。留出空间后牌相应矮一点点。
        var raiseOffset = Math.Max(8, cardHeight / 7);
        usable = Math.Max(40, usable - raiseOffset);
        cardHeight = Math.Max(28, usable / rows);
        raiseOffset = Math.Max(8, cardHeight / 7);

        var cardWidth = Math.Max(20, (int)Math.Round(cardHeight * _renderer.Theme.CardAspect));

        var available = Math.Max(60, Width - 16);

        // 一张牌宽 100% 太多（太散），重叠超过 62% 又看不清花色，夹在中间。
        var step = perRow <= 1
            ? cardWidth
            : Math.Min(
                (int)(cardWidth * 0.62),
                Math.Max(8, (available - cardWidth) / (perRow - 1)));

        _hitBoxes.Clear();

        var index = 0;
        for (var row = 0; row < rows && index < count; row++)
        {
            var inThisRow = Math.Min(perRow, count - index);
            var totalWidth = cardWidth + (step * (inThisRow - 1));
            var startX = Math.Max(8, (Width - totalWidth) / 2);
            var y = raiseOffset + 4 + (row * (cardHeight + rowGap));

            for (var i = 0; i < inThisRow; i++)
            {
                _hitBoxes.Add(new Rectangle(startX + (step * i), y, cardWidth, cardHeight));
            }

            index += inThisRow;
        }

        return new HandLayout(cardWidth, cardHeight, raiseOffset);
    }

    private sealed record HandLayout(int CardWidth, int CardHeight, int RaiseOffset);
}
