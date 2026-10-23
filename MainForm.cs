using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.ComponentModel;
using System.Windows.Forms;

namespace CodexReconnectTool;

internal sealed class MainForm : Form
{
    private static readonly Color Ink = Color.FromArgb(15, 41, 88);
    private static readonly Color Muted = Color.FromArgb(75, 100, 143);
    private static readonly Color Accent = Color.FromArgb(18, 112, 245);
    private static readonly Color Page = Color.FromArgb(242, 246, 252);
    private static readonly Color Border = Color.FromArgb(211, 224, 241);
    private const string UiFontFamily = "Microsoft YaHei UI";
    private const string ServiceUrl = "https://ai.dqtx.cc";
    private const string WeChatId = "dqtx33";

    private readonly EnvConfigStore store = new();
    private readonly TextBox portBox = new();
    private readonly Label proxyStatus = new();
    private readonly Label reachabilityStatus = new();
    private readonly Label homePath = new();
    private readonly Label copyStatus = new();
    private readonly RichTextBox logBox = new();
    private readonly VScrollBar contentScroll = new();
    private Panel contentFlow = null!;
    private bool layingOutCards;
    private Button scanButton = null!;
    private Button testButton = null!;
    private Button applyButton = null!;
    private Button undoButton = null!;
    private Button openFolderButton = null!;
    private Button clearLogButton = null!;

    public MainForm()
    {
        Text = "Codex重连5/5修复助手";
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(800, 740);
        MinimumSize = new Size(760, 640);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        BackColor = Page;
        Font = new Font(UiFontFamily, 10F, FontStyle.Regular);
        DoubleBuffered = true;
        Load += (_, _) =>
        {
            var area = Screen.FromControl(this).WorkingArea;
            Size = new Size(Math.Min(Width, area.Width - 24), Math.Min(Height, area.Height - 24));
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Page,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        Controls.Add(root);

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildContent(), 0, 1);
        root.Controls.Add(BuildFooterPanel(), 0, 2);

        scanButton.Click += async (_, _) => await ScanAsync();
        testButton.Click += async (_, _) => await CheckConnectionAsync();
        applyButton.Click += async (_, _) => await ApplyAsync();
        undoButton.Click += (_, _) => Undo();
        openFolderButton.Click += (_, _) => OpenCodexFolder();
        clearLogButton.Click += (_, _) => logBox.Clear();

        homePath.Text = store.Home;
        undoButton.Enabled = store.HasUndoState;
        var firstCandidate = ProxyService.GetCandidates(store.Home).FirstOrDefault();
        if (firstCandidate is not null)
            portBox.Text = firstCandidate.Port.ToString();

        reachabilityStatus.Text = "测试后显示 CONNECT 结果与 chatgpt.com 响应。";
        reachabilityStatus.ForeColor = Muted;
        AppendLog("工具不会修改 Windows 用户环境变量。写入前会验证本机 HTTP 代理端口。");
    }

    private Control BuildHeader()
    {
        var header = new HeroPanel
        {
            Size = new Size(ClientSize.Width, 104),
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            BackColor = Page
        };
        header.Controls.Add(new Label
        {
            Text = "Codex 连接助手",
            ForeColor = Color.White,
            Font = new Font(UiFontFamily, 22F, FontStyle.Bold),
            AutoSize = false,
            Location = new Point(24, 8),
            Size = new Size(header.Width - 220, 44),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent
        });
        header.Controls.Add(new Label
        {
            Text = "检测本机代理 · 验证连接 · 创建 Codex 配置",
            ForeColor = Color.White,
            Font = new Font(UiFontFamily, 11.5F, FontStyle.Bold),
            AutoSize = false,
            Location = new Point(26, 58),
            Size = new Size(header.Width - 52, 30),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent
        });

        var tagline = new Label
        {
            Text = "简单  ·  高效  ·  稳定",
            AutoSize = false,
            Size = new Size(164, 26),
            ForeColor = Color.White,
            Font = new Font(UiFontFamily, 9.5F, FontStyle.Bold),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(header.Width - 184, 28),
            TextAlign = ContentAlignment.MiddleRight,
            BackColor = Color.Transparent
        };
        header.Controls.Add(tagline);
        return header;
    }

    private Control BuildContent()
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(14, 8, 14, 0),
            BackColor = Page
        };
        contentFlow = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Page,
            Padding = Padding.Empty
        };
        contentFlow.ClientSizeChanged += (_, _) => ResizeCards();
        contentFlow.Layout += (_, _) => ResizeCards();
        host.Controls.Add(contentFlow);
        contentScroll.Dock = DockStyle.Right;
        contentScroll.Visible = false;
        contentScroll.SmallChange = 24;
        contentScroll.ValueChanged += (_, _) => ResizeCards();
        host.Controls.Add(contentScroll);
        contentFlow.MouseWheel += (_, e) =>
        {
            if (!contentScroll.Visible)
                return;
            var maximum = Math.Max(0, contentScroll.Maximum - contentScroll.LargeChange + 1);
            contentScroll.Value = Math.Clamp(contentScroll.Value - Math.Sign(e.Delta) * contentScroll.SmallChange * 3, 0, maximum);
            if (e is HandledMouseEventArgs handled)
                handled.Handled = true;
        };

        contentFlow.Controls.Add(BuildProxyCard());
        contentFlow.Controls.Add(BuildReachabilityCard());
        contentFlow.Controls.Add(BuildHomeCard());
        contentFlow.Controls.Add(BuildLogCard());
        contentFlow.Controls.Add(BuildPromotionCard());
        ResizeCards();
        return host;
    }

    private Control BuildProxyCard()
    {
        var card = MakeCard(132);
        card.Controls.Add(new IconBadge(IconKind.Proxy) { Location = new Point(18, 14) });
        card.Controls.Add(SectionTitle("代理端口", 74, 12));
        card.Controls.Add(SectionSubtitle("设置并测试本机 HTTP 代理端口", 76, 40));
        card.Controls.Add(new Label
        {
            Text = "HTTP 代理端口",
            AutoSize = true,
            ForeColor = Muted,
            Font = new Font(UiFontFamily, 9F),
            Location = new Point(20, 68),
            BackColor = Color.Transparent
        });
        var inputFrame = new RoundedPanel
        {
            Location = new Point(20, 90),
            Size = new Size(136, 34),
            Radius = 6,
            FillColor = Color.White,
            BackColor = Color.White,
            BorderColor = Border
        };
        portBox.Location = new Point(10, 4);
        portBox.Size = new Size(116, 26);
        portBox.BorderStyle = BorderStyle.None;
        portBox.BackColor = Color.White;
        portBox.ForeColor = Ink;
        portBox.Font = new Font(UiFontFamily, 12F, FontStyle.Bold);
        portBox.PlaceholderText = "例如 7897";
        portBox.AccessibleName = "本机代理端口";
        inputFrame.Controls.Add(portBox);
        card.Controls.Add(inputFrame);

        scanButton = MakeButton("自动扫描", false, 126, 34);
        scanButton.Location = new Point(168, 90);
        card.Controls.Add(scanButton);
        testButton = MakeButton("测试连接", true, 126, 34);
        testButton.Location = new Point(306, 90);
        card.Controls.Add(testButton);

        proxyStatus.AutoSize = false;
        proxyStatus.Visible = false;
        proxyStatus.Location = new Point(454, 74);
        proxyStatus.Size = new Size(card.Width - 474, 52);
        proxyStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        proxyStatus.ForeColor = Muted;
        proxyStatus.BackColor = Color.FromArgb(239, 245, 253);
        proxyStatus.Font = new Font(UiFontFamily, 9F);
        proxyStatus.TextAlign = ContentAlignment.MiddleLeft;
        proxyStatus.Padding = new Padding(10, 4, 8, 4);
        card.Controls.Add(proxyStatus);
        return card;
    }

    private Control BuildReachabilityCard()
    {
        var card = MakeCard(84);
        card.Controls.Add(new IconBadge(IconKind.Verify) { Location = new Point(18, 18) });
        card.Controls.Add(SectionTitle("连接验证", 74, 10));
        reachabilityStatus.AutoSize = false;
        reachabilityStatus.Location = new Point(76, 40);
        reachabilityStatus.Size = new Size(card.Width - 96, 36);
        reachabilityStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        reachabilityStatus.Font = new Font(UiFontFamily, 9.5F);
        reachabilityStatus.BackColor = Color.Transparent;
        card.Controls.Add(reachabilityStatus);
        return card;
    }

    private Control BuildHomeCard()
    {
        var card = MakeCard(84);
        card.Controls.Add(new IconBadge(IconKind.Config) { Location = new Point(18, 22) });
        card.Controls.Add(SectionTitle("Codex 配置位置", 74, 10));
        homePath.AutoSize = false;
        homePath.Location = new Point(76, 42);
        homePath.Size = new Size(card.Width - 96, 28);
        homePath.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        homePath.Font = new Font("Consolas", 10F);
        homePath.ForeColor = Ink;
        homePath.BackColor = Color.FromArgb(242, 247, 254);
        homePath.BorderStyle = BorderStyle.None;
        homePath.TextAlign = ContentAlignment.MiddleLeft;
        homePath.Padding = new Padding(10, 0, 8, 0);
        homePath.AutoEllipsis = true;
        card.Controls.Add(homePath);
        return card;
    }

    private Control BuildLogCard()
    {
        var card = MakeCard(112);
        card.Controls.Add(new IconBadge(IconKind.Logs) { Location = new Point(18, 10) });
        card.Controls.Add(SectionTitle("执行记录", 74, 16));
        clearLogButton = MakeButton("清空记录", false, 100, 30);
        clearLogButton.Location = new Point(card.Width - 120, 14);
        clearLogButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        card.Controls.Add(clearLogButton);

        logBox.Location = new Point(20, 58);
        logBox.Size = new Size(card.Width - 40, 44);
        logBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        logBox.ReadOnly = true;
        logBox.BorderStyle = BorderStyle.None;
        logBox.BackColor = Color.FromArgb(240, 246, 253);
        logBox.ForeColor = Color.FromArgb(38, 61, 102);
        logBox.Font = new Font("Consolas", 9F);
        logBox.DetectUrls = false;
        logBox.ScrollBars = RichTextBoxScrollBars.Vertical;
        card.Controls.Add(logBox);
        return card;
    }

    private Control BuildPromotionCard()
    {
        var card = MakeCard(108, Color.FromArgb(248, 248, 255));
        card.Controls.Add(new IconBadge(IconKind.Promo) { Location = new Point(18, 34) });
        var title = SectionTitle("GPT 自助代充", 74, 10);
        card.Controls.Add(title);
        var recommendation = new Label
        {
            Text = "推荐",
            TextAlign = ContentAlignment.MiddleCenter,
            AutoSize = false,
            Size = new Size(42, 20),
            Location = new Point(74 + TextRenderer.MeasureText(title.Text, title.Font).Width + 8, 15),
            ForeColor = Color.FromArgb(92, 60, 222),
            BackColor = Color.FromArgb(237, 231, 255),
            Font = new Font(UiFontFamily, 8.5F, FontStyle.Bold)
        };
        card.Controls.Add(recommendation);
        card.Controls.Add(new Label
        {
            Text = "支持支付宝 / 微信付款 · 无需账密 · 自助充值",
            AutoSize = true,
            ForeColor = Muted,
            Font = new Font(UiFontFamily, 9.5F),
            Location = new Point(76, 42),
            BackColor = Color.Transparent
        });
        card.Controls.Add(new Label
        {
            Text = "遇到其他问题，直接加微信：dqtx33",
            AutoSize = true,
            ForeColor = Muted,
            Font = new Font(UiFontFamily, 9.5F),
            Location = new Point(76, 65),
            BackColor = Color.Transparent
        });
        var websiteButton = MakeButton("前往 ai.dqtx.cc", true, 154, 34);
        websiteButton.Location = new Point(card.Width - 174, 14);
        websiteButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        websiteButton.Click += (_, _) => OpenServiceWebsite();
        card.Controls.Add(websiteButton);

        var copyButton = MakeButton("复制微信号", false, 154, 30);
        copyButton.Location = new Point(card.Width - 174, 56);
        copyButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        copyButton.Click += (_, _) => CopyWeChatId();
        card.Controls.Add(copyButton);
        copyStatus.AutoSize = true;
        copyStatus.ForeColor = Muted;
        copyStatus.Font = new Font(UiFontFamily, 8.5F);
        copyStatus.Location = new Point(76, 87);
        copyStatus.BackColor = Color.Transparent;
        card.Controls.Add(copyStatus);
        return card;
    }

    private Control BuildFooterPanel()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Page,
            Margin = Padding.Empty,
            Padding = new Padding(10, 6, 10, 12)
        };
        for (var i = 0; i < 3; i++)
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 3));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        applyButton = MakeButton("创建 .env", true, height: 42);
        undoButton = MakeButton("撤销并恢复", false, height: 42);
        openFolderButton = MakeButton("Codex 目录", false, height: 42);
        foreach (var button in new[] { applyButton, undoButton, openFolderButton })
        {
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(4, 0, 4, 0);
        }
        row.Controls.Add(applyButton, 0, 0);
        row.Controls.Add(undoButton, 1, 0);
        row.Controls.Add(openFolderButton, 2, 0);
        return row;
    }

    private void ResizeCards()
    {
        if (contentFlow is null || contentFlow.IsDisposed || layingOutCards)
            return;

        layingOutCards = true;
        try
        {
            var totalHeight = contentFlow.Controls.Cast<Control>().Sum(control => control.Height + control.Margin.Bottom);
            var viewportHeight = Math.Max(1, contentFlow.ClientSize.Height);
            contentScroll.Visible = totalHeight > viewportHeight;
            contentScroll.Maximum = Math.Max(0, totalHeight - 1);
            contentScroll.LargeChange = viewportHeight;
            contentScroll.Value = Math.Min(contentScroll.Value, Math.Max(0, totalHeight - viewportHeight));
            var width = Math.Max(1, contentFlow.ClientSize.Width - 2);
            var top = -contentScroll.Value;
            foreach (Control control in contentFlow.Controls)
            {
                var bounds = new Rectangle(0, top, width, control.Height);
                if (control.Bounds != bounds)
                    control.Bounds = bounds;
                top += control.Height + control.Margin.Bottom;
            }
        }
        finally
        {
            layingOutCards = false;
        }
    }

    private RoundedPanel MakeCard(int height, Color? fillColor = null) => new()
    {
        Width = Math.Max(1, ClientSize.Width - 28),
        Height = height,
        FillColor = fillColor ?? Color.White,
        BorderColor = Border,
        Radius = 10,
        Margin = new Padding(0, 0, 0, 8),
        Padding = Padding.Empty,
        BackColor = fillColor ?? Color.White,
        BorderStyle = BorderStyle.None,
        TabStop = false
    };

    private static Label SectionTitle(string text, int x, int y) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Ink,
        Font = new Font(UiFontFamily, 13.5F, FontStyle.Bold),
        Location = new Point(x, y),
        BackColor = Color.Transparent
    };

    private static Label SectionSubtitle(string text, int x, int y) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Muted,
        Font = new Font(UiFontFamily, 9.5F),
        Location = new Point(x, y),
        BackColor = Color.Transparent
    };

    private static Button MakeButton(string text, bool primary, int width = 154, int height = 34) => new RefinedButton(primary)
    {
        Text = text,
        Width = width,
        Height = height,
        BackColor = primary ? Accent : Color.White,
        ForeColor = primary ? Color.White : Ink,
        Font = new Font(UiFontFamily, 10F, FontStyle.Bold),
        Cursor = Cursors.Hand,
        AccessibleName = text
    };

    private static GraphicsPath MakeRoundPath(Rectangle bounds, int radius)
    {
        var diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class RefinedButton : Button
    {
        private readonly bool primary;
        private bool hovered;
        private bool pressed;

        public RefinedButton(bool primary)
        {
            this.primary = primary;
            SetStyle(ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            MouseEnter += (_, _) => { hovered = true; Invalidate(); };
            MouseLeave += (_, _) => { hovered = false; pressed = false; Invalidate(); };
            MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } };
            MouseUp += (_, _) => { pressed = false; Invalidate(); };
            KeyDown += (_, e) => { if (e.KeyCode == Keys.Space) { pressed = true; Invalidate(); } };
            KeyUp += (_, _) => { pressed = false; Invalidate(); };
            GotFocus += (_, _) => Invalidate();
            LostFocus += (_, _) => { pressed = false; Invalidate(); };
            EnabledChanged += (_, _) => { hovered = false; pressed = false; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var background = new SolidBrush(Parent?.BackColor ?? Page))
                e.Graphics.FillRectangle(background, ClientRectangle);

            if (Width < 3 || Height < 3)
                return;

            var fill = !Enabled ? Color.FromArgb(240, 243, 248)
                : primary
                    ? pressed ? Color.FromArgb(10, 82, 190) : hovered ? Color.FromArgb(14, 98, 224) : BackColor
                    : pressed ? Color.FromArgb(227, 237, 252) : hovered ? Color.FromArgb(240, 245, 254) : BackColor;
            var outline = !Enabled ? Color.FromArgb(220, 228, 239)
                : primary ? fill : hovered ? Color.FromArgb(153, 182, 226) : Border;
            var textColor = Enabled ? ForeColor : Color.FromArgb(139, 153, 174);
            var bounds = new Rectangle(1, 1, Width - 3, Height - 3);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = MakeRoundPath(bounds, 6))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(outline))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(8, 0, Width - 16, Height), textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix |
                TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform);
            if (Focused && ShowFocusCues)
            {
                using var focusPath = MakeRoundPath(Rectangle.Inflate(bounds, -3, -3), 4);
                using var focusPen = new Pen(primary ? Color.FromArgb(210, 232, 255) : Accent);
                e.Graphics.DrawPath(focusPen, focusPath);
            }
        }
    }

    private enum IconKind { Proxy, Verify, Config, Logs, Promo }

    private sealed class HeroPanel : Panel
    {
        public HeroPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BorderStyle = BorderStyle.None;
            TabStop = false;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (Width < 2 || Height < 2)
                return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new LinearGradientBrush(ClientRectangle,
                Color.FromArgb(20, 52, 99), Color.FromArgb(31, 101, 202), 0f);
            e.Graphics.FillRectangle(brush, ClientRectangle);
        }
    }

    private sealed class RoundedPanel : Panel
    {
        private int radius = 10;
        private Region? shapeRegion;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color FillColor { get; set; } = Color.White;
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color BorderColor { get; set; } = Border;
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Radius
        {
            get => radius;
            set
            {
                radius = value;
                UpdateShapeRegion();
                Invalidate();
            }
        }

        public RoundedPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BorderStyle = BorderStyle.None;
            TabStop = false;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateShapeRegion();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var outer = new SolidBrush(Parent?.BackColor ?? Page))
                e.Graphics.FillRectangle(outer, ClientRectangle);

            if (Width < 2 || Height < 2)
                return;
            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = MakeRoundPath(bounds, Radius);
            using var fill = new SolidBrush(FillColor);
            using var border = new Pen(BorderColor, 1);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }

        private void UpdateShapeRegion()
        {
            if (Width < 2 || Height < 2)
                return;

            using var path = MakeRoundPath(new Rectangle(0, 0, Width - 1, Height - 1), radius);
            var next = new Region(path);
            var previous = shapeRegion;
            shapeRegion = next;
            Region = next;
            previous?.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                shapeRegion?.Dispose();
                shapeRegion = null;
            }
            base.Dispose(disposing);
        }
    }

    private sealed class IconBadge : Control
    {
        private readonly IconKind kind;

        public IconBadge(IconKind kind)
        {
            this.kind = kind;
            Size = new Size(44, 44);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            TabStop = false;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? Color.White);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var (background, color) = kind switch
            {
                IconKind.Proxy => (Color.FromArgb(230, 241, 255), Color.FromArgb(21, 112, 236)),
                IconKind.Verify => (Color.FromArgb(222, 248, 239), Color.FromArgb(17, 165, 111)),
                IconKind.Config => (Color.FromArgb(238, 234, 255), Color.FromArgb(91, 71, 226)),
                IconKind.Logs => (Color.FromArgb(233, 238, 246), Color.FromArgb(54, 74, 111)),
                _ => (Color.FromArgb(239, 234, 255), Color.FromArgb(105, 74, 232))
            };
            using (var path = MakeRoundPath(new Rectangle(0, 0, Width - 1, Height - 1), 10))
            using (var fill = new SolidBrush(background))
                e.Graphics.FillPath(fill, path);
            using var pen = new Pen(color, 2.2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            using var solid = new SolidBrush(color);
            var glyphState = e.Graphics.Save();
            var scale = Math.Min(Width, Height) / 44f;
            using var glyphTransform = new Matrix(scale, 0, 0, scale,
                (Width - 40 * scale) / 2, (Height - 40 * scale) / 2);
            e.Graphics.MultiplyTransform(glyphTransform);

            switch (kind)
            {
                case IconKind.Proxy:
                    e.Graphics.DrawBezier(pen, 6, 16, 14, 7, 26, 7, 34, 16);
                    e.Graphics.DrawBezier(pen, 11, 21, 16, 15, 24, 15, 29, 21);
                    e.Graphics.DrawBezier(pen, 16, 26, 18, 23, 22, 23, 24, 26);
                    e.Graphics.FillEllipse(solid, 17.6f, 29, 4.8f, 4.8f);
                    break;
                case IconKind.Verify:
                    var shield = new[]
                    {
                        new PointF(20, 6), new PointF(32, 11), new PointF(31, 22),
                        new PointF(28, 29), new PointF(20, 35), new PointF(12, 29),
                        new PointF(9, 22), new PointF(8, 11)
                    };
                    e.Graphics.DrawPolygon(pen, shield);
                    e.Graphics.DrawLines(pen, new[]
                    {
                        new PointF(14, 20), new PointF(19, 25), new PointF(27, 16)
                    });
                    break;
                case IconKind.Config:
                    using (var doc = new GraphicsPath())
                    {
                        doc.AddLines(new[]
                        {
                            new Point(11, 6), new Point(24, 6), new Point(31, 13),
                            new Point(31, 34), new Point(11, 34)
                        });
                        doc.CloseFigure();
                        e.Graphics.DrawPath(pen, doc);
                    }
                    e.Graphics.DrawLines(pen, new[] { new Point(24, 6), new Point(24, 13), new Point(31, 13) });
                    e.Graphics.DrawLine(pen, 15, 18, 26, 18);
                    e.Graphics.DrawLine(pen, 15, 23, 26, 23);
                    e.Graphics.DrawLine(pen, 15, 28, 22, 28);
                    break;
                case IconKind.Logs:
                    using (var terminal = MakeRoundPath(new Rectangle(7, 10, 26, 22), 4))
                        e.Graphics.DrawPath(pen, terminal);
                    e.Graphics.DrawLines(pen, new[]
                    {
                        new PointF(12, 16), new PointF(17, 20), new PointF(12, 24)
                    });
                    e.Graphics.DrawLine(pen, 21, 25, 28, 25);
                    break;
                case IconKind.Promo:
                    e.Graphics.FillPolygon(solid, Star(20, 20, 12, 4.5f, 4));
                    e.Graphics.FillPolygon(solid, Star(30, 9, 4, 1.5f, 4));
                    e.Graphics.FillPolygon(solid, Star(8, 31, 3, 1.2f, 4));
                    break;
            }
            e.Graphics.Restore(glyphState);
        }

        private static PointF[] Star(float cx, float cy, float outer, float inner, int points)
        {
            var result = new PointF[points * 2];
            for (var i = 0; i < result.Length; i++)
            {
                var angle = -Math.PI / 2 + i * Math.PI / points;
                var radius = i % 2 == 0 ? outer : inner;
                result[i] = new PointF(cx + (float)Math.Cos(angle) * radius, cy + (float)Math.Sin(angle) * radius);
            }
            return result;
        }
    }
    private async Task ScanAsync()
    {
        SetBusy(true);
        try
        {
            var candidates = ProxyService.GetCandidates(store.Home);
            AppendLog($"发现 {candidates.Count} 个候选端口，正在检查 HTTP CONNECT…");
            proxyStatus.Text = "正在扫描本机代理端口…";
            proxyStatus.ForeColor = Muted;

            var results = await ProxyService.ScanAsync(store.Home, CancellationToken.None);
            var working = results.FirstOrDefault(item => item.Result.IsWorking);
            if (working.Candidate is not null)
            {
                portBox.Text = working.Candidate.Port.ToString();
                proxyStatus.Text = $"已确认 127.0.0.1:{working.Candidate.Port}（{working.Candidate.Source}）返回 CONNECT 200。";
                proxyStatus.ForeColor = Color.FromArgb(25, 135, 84);
                AppendLog($"通过：127.0.0.1:{working.Candidate.Port} 返回 HTTP CONNECT 200。");
                await ShowReachabilityAsync(working.Candidate.Port);
            }
            else
            {
                var authRequired = results.FirstOrDefault(item => item.Result.StatusCode == 407);
                if (authRequired.Candidate is not null)
                {
                    portBox.Text = authRequired.Candidate.Port.ToString();
                    proxyStatus.Text = $"端口 {authRequired.Candidate.Port} 要求代理认证（HTTP 407），本工具无法验证凭据。";
                    proxyStatus.ForeColor = Color.DarkOrange;
                    AppendLog($"端口 {authRequired.Candidate.Port} 返回 HTTP 407；请检查代理认证设置。");
                }
                else
                {
                    proxyStatus.Text = "未找到返回 CONNECT 200 的端口。请启动代理软件，或手动输入端口后测试。";
                    proxyStatus.ForeColor = Color.DarkOrange;
                    AppendLog("没有找到可用的 HTTP CONNECT 代理端口。");
                }
            }
        }
        catch (Exception ex)
        {
            proxyStatus.Text = "自动扫描失败；可以手动输入代理端口。";
            proxyStatus.ForeColor = Color.DarkOrange;
            AppendLog("扫描失败：" + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task CheckConnectionAsync()
    {
        if (!TryGetPort(out var port))
            return;

        SetBusy(true);
        try
        {
            var probe = await ProxyService.ProbeHttpProxyAsync(port, CancellationToken.None);
            if (probe.IsWorking)
            {
                proxyStatus.Text = $"127.0.0.1:{port} 返回 CONNECT 200，HTTP 代理端口可用。";
                proxyStatus.ForeColor = Color.FromArgb(25, 135, 84);
                AppendLog($"代理端口 {port}：CONNECT 200。");
                await ShowReachabilityAsync(port);
            }
            else
            {
                var detail = probe.StatusCode.HasValue
                    ? $"返回 HTTP {probe.StatusCode}，不是可直接使用的 CONNECT 200。"
                    : probe.Error ?? "没有收到 HTTP 代理响应。";
                proxyStatus.Text = detail;
                proxyStatus.ForeColor = Color.DarkOrange;
                reachabilityStatus.Text = "代理端口未通过验证。";
                reachabilityStatus.ForeColor = Color.DarkOrange;
                AppendLog($"代理端口 {port} 未通过：{detail}");
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ApplyAsync()
    {
        if (!TryGetPort(out var port))
            return;

        SetBusy(true);
        try
        {
            proxyStatus.Text = "正在重新验证代理端口…";
            proxyStatus.ForeColor = Muted;
            var probe = await ProxyService.ProbeHttpProxyAsync(port, CancellationToken.None);
            if (!probe.IsWorking)
            {
                var detail = probe.StatusCode.HasValue ? $"HTTP {probe.StatusCode}" : probe.Error ?? "无响应";
                proxyStatus.Text = $"代理端口未通过验证：{detail}";
                proxyStatus.ForeColor = Color.DarkOrange;
                AppendLog($"没有写入配置：代理端口 {port} 未返回 CONNECT 200。");
                MessageBox.Show(this,
                    $"代理端口 127.0.0.1:{port} 未返回 CONNECT 200（{detail}）。\n配置没有改动。",
                    "代理验证失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var reachability = await ProxyService.TestChatGptAsync(port, CancellationToken.None);
            if (!reachability.ReceivedResponse)
            {
                var answer = MessageBox.Show(this,
                    $"代理端口可用，但通过它访问 chatgpt.com 的测试失败（{reachability.Error}）。\n\n仍要创建或更新 .env 吗？这不代表 Codex 一定能连接。",
                    "目标站点测试失败", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes)
                {
                    AppendLog("用户取消写入：chatgpt.com 测试没有收到响应。");
                    return;
                }
            }
            else
            {
                reachabilityStatus.Text = $"chatgpt.com 返回 HTTP {reachability.StatusCode}；已收到响应，但这不验证登录状态或完整功能。";
                reachabilityStatus.ForeColor = Color.FromArgb(25, 135, 84);
            }

            var backupNote = store.Apply(port);
            proxyStatus.Text = $"已将代理端口 {port} 写入 Codex 专属 .env。";
            proxyStatus.ForeColor = Color.FromArgb(25, 135, 84);
            undoButton.Enabled = true;
            AppendLog($"配置已保存：{store.EnvPath}");
            AppendLog(backupNote);
            AppendLog("Windows 用户环境变量未修改。请手动关闭 Codex，从原来的入口重新打开，再新建对话验证。");
            MessageBox.Show(this,
                $"代理配置已保存到 Codex 的 .env。\n\n{backupNote}\n\n请手动完全关闭 Codex，从原来的入口重新打开，再新建一个对话验证。",
                "配置已保存", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppendLog("写入失败：" + ex.Message);
            MessageBox.Show(this, "没有完成配置写入：\n" + ex.Message, "写入失败",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Undo()
    {
        if (!store.HasUndoState)
        {
            MessageBox.Show(this, "没有找到本工具的撤销记录。", "无需撤销",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            undoButton.Enabled = false;
            return;
        }

        var changed = store.HasUserEditsSinceApply();
        var warning = changed
            ? "\n\n检测到 .env 在本工具写入后又发生了变化。继续会用备份覆盖当前文件，这些后续修改将丢失。"
            : string.Empty;
        var answer = MessageBox.Show(this,
            "确定恢复本工具修改前的 .env 配置吗？" + warning,
            "确认撤销", MessageBoxButtons.YesNo, changed ? MessageBoxIcon.Warning : MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
            return;

        try
        {
            var message = store.Undo(allowReplacingEditedFile: changed);
            AppendLog(message);
            undoButton.Enabled = false;
            MessageBox.Show(this, message, "已撤销", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppendLog("撤销失败：" + ex.Message);
            MessageBox.Show(this, ex.Message, "撤销失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ShowReachabilityAsync(int port)
    {
        reachabilityStatus.Text = "正在通过代理请求 chatgpt.com…";
        reachabilityStatus.ForeColor = Muted;
        var result = await ProxyService.TestChatGptAsync(port, CancellationToken.None);
        if (result.ReceivedResponse)
        {
            reachabilityStatus.Text = $"chatgpt.com 返回 HTTP {result.StatusCode}；收到响应不代表登录状态或完整功能正常。";
            reachabilityStatus.ForeColor = Color.FromArgb(25, 135, 84);
            AppendLog($"chatgpt.com 返回 HTTP {result.StatusCode}。");
        }
        else
        {
            reachabilityStatus.Text = $"代理端口正常，但访问 chatgpt.com 失败（{result.Error}）。可以换节点后重试。";
            reachabilityStatus.ForeColor = Color.DarkOrange;
            AppendLog($"chatgpt.com 测试失败：{result.Error}。");
        }
    }

    private bool TryGetPort(out int port)
    {
        if (!int.TryParse(portBox.Text.Trim(), out port) || port is < 1 or > 65535)
        {
            MessageBox.Show(this, "请输入 1 到 65535 之间的本机代理端口号。", "端口无效",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            portBox.Focus();
            return false;
        }
        return true;
    }

    private void CopyWeChatId()
    {
        try
        {
            Clipboard.SetText(WeChatId);
            copyStatus.Text = "联系微信号 dqtx33 有折扣";
            AppendLog("已复制微信号到剪贴板。");
        }
        catch (Exception ex)
        {
            copyStatus.Text = "复制失败：" + ex.Message;
        }
    }

    private void OpenCodexFolder()
    {
        if (!Directory.Exists(store.Home))
        {
            MessageBox.Show(this,
                $"Codex 配置目录尚不存在：\n{store.Home}\n\n如果 Codex 尚未创建配置目录，请先启动 Codex。",
                "目录不存在", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            startInfo.ArgumentList.Add(store.Home);
            Process.Start(startInfo);
            AppendLog($"已打开 Codex 配置目录：{store.Home}");
        }
        catch (Exception ex)
        {
            AppendLog("打开 Codex 目录失败：" + ex.Message);
            MessageBox.Show(this, "无法打开 Codex 目录：\n" + ex.Message, "打开失败",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenServiceWebsite()
    {
        try
        {
            Process.Start(new ProcessStartInfo(ServiceUrl) { UseShellExecute = true });
            AppendLog("已在默认浏览器中打开 GPT 自助充值页面。");
        }
        catch (Exception ex)
        {
            AppendLog("打开充值页面失败：" + ex.Message);
            MessageBox.Show(this, "无法打开充值页面：\n" + ex.Message, "打开失败",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SetBusy(bool busy)
    {
        if (busy)
            proxyStatus.Visible = true;
        scanButton.Enabled = !busy;
        testButton.Enabled = !busy;
        applyButton.Enabled = !busy;
        undoButton.Enabled = !busy && store.HasUndoState;
        openFolderButton.Enabled = !busy;
        clearLogButton.Enabled = !busy;
        portBox.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private void AppendLog(string text)
    {
        logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
        logBox.SelectionStart = logBox.TextLength;
        logBox.ScrollToCaret();
    }
}
