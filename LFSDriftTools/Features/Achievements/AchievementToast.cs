namespace LFSDriftTools
{
    public sealed class AchievementToast
    {
        public string Title { get; init; } = "";
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public string MethodLabel { get; init; } = "";
        public InputMethod Method { get; init; }
        public AchievementIcon Icon { get; init; }
        public AchievementRarity Rarity { get; init; }
    }
}
