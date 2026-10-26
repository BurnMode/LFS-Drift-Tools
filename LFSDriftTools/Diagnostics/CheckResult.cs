namespace LFSDriftTools
{
    public enum CheckStatus { Ok, Info, Warning, Error }

    public sealed class CheckResult
    {
        public CheckStatus Status { get; init; }
        public string Title { get; init; } = "";
        public string Detail { get; init; } = "";
        public string Help { get; init; } = "";
    }
}
