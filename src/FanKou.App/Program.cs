using System.Drawing.Imaging;
using FanKou.Ai;
using FanKou.Core.Config;
using FanKou.Core.Game;
using FanKou.Core.Util;
using FanKou.Ui.Dialogs;
using FanKou.Ui.Rendering;

namespace FanKou.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // 出事了至少留个文件下来。这游戏本来没有任何日志落地——
        //「牌局记录」也只是内存里的一份，程序一关就没了，事后无从查起。
        AppDomain.CurrentDomain.UnhandledException += (_, e) => WriteCrashLog(e.ExceptionObject as Exception);
        Application.ThreadException += (_, e) => WriteCrashLog(e.Exception);

        try
        {
            Run(args);
        }
        catch (Exception ex)
        {
            WriteCrashLog(ex);
            throw;
        }
    }

    /// <summary>
    /// 把没接住的异常追加到 exe 旁边的 crash.log。
    /// 写在那儿是因为这程序是绿色版，用户知道它放在哪。
    /// </summary>
    private static void WriteCrashLog(Exception? ex)
    {
        if (ex is null)
        {
            return;
        }

        try
        {
            File.AppendAllText(
                Path.Combine(AppContext.BaseDirectory, "crash.log"),
                $"===== {DateTime.Now:yyyy-MM-dd HH:mm:ss} ====={Environment.NewLine}"
                + $"{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // 连日志都写不出来就算了，别在崩溃路径上再抛一个。
        }
    }

    private static void Run(string[] args)
    {
        // 开发用：把各种牌画成一张图存下来，肉眼检查渲染效果。
        //   FanKou.App.exe --render-sheet 牌面.png [样式目录]
        if (args.Length > 0 && args[0] == "--render-sheet")
        {
            RenderSheet(args);
            return;
        }

        ApplicationConfiguration.Initialize();

        // 开发用：把主界面连同一个假局面渲染成图片，检查布局。
        if (args.Length > 0 && args[0] == "--render-ui")
        {
            RenderUi(
                args.Length > 1 ? args[1] : "ui.png",
                withTrick: args.Length <= 2 || args[2] != "bidding");
            return;
        }

        // 开发用：把换底牌的对话框渲染成图片，检查排版。
        //   FanKou.App.exe --render-swap 换底牌.png [样式根目录]
        if (args.Length > 0 && args[0] == "--render-swap")
        {
            RenderSwap(args);
            return;
        }

        // 开发用：把「规则说明」对话框渲染成图片，顺便验证嵌入的《玩法说明.md》读得到。
        if (args.Length > 0 && args[0] == "--render-rules")
        {
            RenderRules(
                args.Length > 1 ? args[1] : "rules.png",
                detail: args.Length > 2 && args[2] == "detail");
            return;
        }

        // 开发用：主程序自己把整局打完（连人类座位也交给 AI），
        // 用来验证"引擎 + 控制器 + 计时器"这一整条路不会卡死。结果写到文件里。
        if (args.Length > 0 && args[0] == "--autoplay")
        {
            AutoPlay(args);
            return;
        }

        Application.Run(new MainForm());
    }

    private static void AutoPlay(string[] args)
    {
        var rounds = args.Length > 1 ? int.Parse(args[1]) : 3;
        var output = args.Length > 2 ? args[2] : "autoplay.log";

        var lines = new List<string>();
        var rng = SharedRandomSource.Instance;
        var options = RuleOptionsLoader.LoadFromBaseDirectory();
        var engine = new GameEngine(rng, options);

        var brains = Seats.All.ToDictionary(
            seat => seat,
            _ => (IPlayerBrain)new SimpleBrain(rng));

        var controller = new GameController(engine, brains, Seat.South, options);
        controller.SetAutoPlay(true);

        var played = 0;
        controller.RoundFinished += summary =>
        {
            played++;
            lines.Add($"第 {summary.RoundNumber} 局：{summary.OutcomeText}｜闲家 {summary.DefenderPoints} 分｜{summary.BottomText}");
            lines.Add($"  等级 {summary.LevelBefore} → {summary.LevelAfter}");

            if (played >= rounds || summary.RoomOver)
            {
                lines.Add($"完成：共 {played} 局，等级 东西 {engine.LevelOf(Team.EastWest)} / 南北 {engine.LevelOf(Team.NorthSouth)}");
                File.WriteAllLines(output, lines);
                controller.Dispose();
                Application.Exit();
            }
            else
            {
                controller.StartNextRound();
            }
        };

        controller.Start();
        Application.Run();
    }

    private static void RenderSwap(string[] args)
    {
        var output = args.Length > 1 ? args[1] : "swap.png";

        // 指定样式根目录就用那儿发现的外挂样式，否则扫默认位置。
        // 有外挂样式时取最后一套——头一套永远是「程序自带」，
        // 这样才能检查"换上真实牌面图之后"的排版，而不只是自带画法。
        var skins = args.Length > 2 ? CardSkin.Discover([args[2]]) : CardSkin.Discover();
        var skin = skins.Count > 1 ? skins[^1] : skins[0];

        using var renderer = new CardRenderer(skin);
        var (hand, bottom, locked) = DemoState.SwapSample();
        // 先选中开头 3 张，好检查"浮起来"的那一截会不会被切掉。
        using var dialog = BottomSwapDialog.CreateForPreview(renderer, hand, bottom, locked, selectFirst: 3);

        dialog.Show();
        Application.DoEvents();

        using var bitmap = new Bitmap(dialog.Width, dialog.Height);
        dialog.DrawToBitmap(bitmap, new Rectangle(0, 0, dialog.Width, dialog.Height));
        bitmap.Save(output, ImageFormat.Png);

        dialog.Close();
    }

    private static void RenderUi(string output, bool withTrick)
    {
        using var form = new MainForm();
        form.LoadDemoState(withTrick);
        form.Show();
        Application.DoEvents();

        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
        bitmap.Save(output, ImageFormat.Png);

        form.Close();
    }

    private static void RenderRules(string output, bool detail)
    {
        using var renderer = new CardRenderer(CardSkin.BuiltIn);
        using var dialog = RulesDialog.CreateForPreview(renderer, detail);
        dialog.Show();
        Application.DoEvents();

        using var bitmap = new Bitmap(dialog.Width, dialog.Height);
        dialog.DrawToBitmap(bitmap, new Rectangle(0, 0, dialog.Width, dialog.Height));
        bitmap.Save(output, ImageFormat.Png);

        dialog.Close();
    }

    private static void RenderSheet(string[] args)
    {
        var output = args.Length > 1 ? args[1] : "card-sheet.png";

        var skins = args.Length > 2
            ? CardSkin.Discover([args[2]])
            : CardSkin.Discover();

        using var renderer = new CardRenderer(skins[0]);
        using var sheet = CardSheet.Render(renderer, Theme.Default, 150, $"自带牌面 · 可用样式 {skins.Count} 套");
        sheet.Save(output, ImageFormat.Png);
    }
}
