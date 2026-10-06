namespace FanKou.Core.Play;

public sealed record ValidationResult(bool IsValid, string Reason)
{
    public static readonly ValidationResult Ok = new(true, string.Empty);

    public static ValidationResult Fail(string reason) => new(false, reason);

    public override string ToString() => IsValid ? "合法" : Reason;
}
