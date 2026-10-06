namespace FanKou.Core.Config;

public static class RuleOptionsLoader
{
    public const string FileName = "rules.json";

    /// <summary>
    /// 从 exe 所在目录读 rules.json。文件不存在或读坏了都退回默认值——
    /// 规则开关不该让游戏起不来。
    /// </summary>
    public static RuleOptions LoadFromBaseDirectory() =>
        Load(Path.Combine(AppContext.BaseDirectory, FileName));

    public static RuleOptions Load(string path)
    {
        try
        {
            return File.Exists(path) ? RuleOptions.FromJson(File.ReadAllText(path)) : RuleOptions.Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or System.Text.Json.JsonException)
        {
            return RuleOptions.Default;
        }
    }
}
