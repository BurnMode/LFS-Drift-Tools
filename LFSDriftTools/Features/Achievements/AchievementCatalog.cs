using System.Collections.Generic;

namespace LFSDriftTools
{
    public enum AchievementVisibility
    {
        Visible,
        Hidden,
        Secret,
    }

    public enum AchievementRarity
    {
        Common,
        Rare,
        Epic,
        Legendary,
    }

    public enum AchievementIcon
    {
        DriftCar,
        Speedometer,
        Rocket,
        Tachometer,
        Cone,
        Heart,
        UpsideDown,
        Reset,
        Wings,
        Owl,
        Question,
        Donut,
        Protractor,
        ReverseArrow,
        Cannonball,
        Bricks,
        Handbrake,
        Speaker,
        Blinker,
        Lightning,
        Barrel,
        Smoke,
        Stopwatch,
        Carousel,
        SmokeDrift,
        Planet,
        Airplane,
        Cat,
        ConeStack,
        TyreStack,
        Barrier,
        RoadSign,
        Bin,
        HayBale,
        Ambulance,
        BowlingPins,
        Medal,
        Trophy,
        ComboMax,
        Crown,
        BonusStack,
        PitSign,
        Pileup,
        Warning,
        WrongWay,
    }

    public enum InputMethod
    {
        Wheel,
        Pad,
        KeyboardMouse,
    }

    public sealed class AchievementDefinition
    {
        public AchievementDefinition(string id, AchievementVisibility visibility, AchievementRarity rarity, AchievementIcon icon, long target = 0)
        {
            Id = id;
            Visibility = visibility;
            Rarity = rarity;
            Icon = icon;
            Target = target;
        }

        public string Id { get; }
        public AchievementVisibility Visibility { get; }
        public AchievementRarity Rarity { get; }
        public AchievementIcon Icon { get; }
        public long Target { get; }

        public string NameKey => $"ach.{Id}.name";
        public string DescriptionKey => $"ach.{Id}.desc";
        public string HintKey => $"ach.{Id}.hint";
    }

    public static class AchievementCatalog
    {
        public const string DriftFirst = "drift_first";
        public const string Speed200 = "speed_200";
        public const string Speed300 = "speed_300";
        public const string RevCuts1000 = "rev_cuts_1000";
        public const string Cones100 = "cones_100";
        public const string Kiss = "kiss";
        public const string Roof = "roof";
        public const string Resets20 = "resets_20";
        public const string Air1 = "air_1";
        public const string NightOwl = "night_owl";
        public const string Donut10 = "donut_10";
        public const string Drift90 = "drift_90";
        public const string Reverse60 = "reverse_60";
        public const string TBone = "tbone";
        public const string Wall = "wall";
        public const string Handbrake250 = "handbrake_250";
        public const string RevHold10 = "rev_hold_10";
        public const string Blinker2Km = "blinker_2km";
        public const string Launch = "launch";
        public const string BarrelRoll = "barrel_roll";
        public const string BurnoutSmokehouse = "burnout_smokehouse";
        public const string BurnoutMarathon = "burnout_marathon";
        public const string BurnoutCarousel = "burnout_carousel";
        public const string BurnoutToDrift = "burnout_to_drift";
        public const string Air3 = "air_3";
        public const string AirTotal60 = "air_total_60";
        public const string RoofLanding = "roof_landing";
        public const string Cones1000 = "cones_1000";
        public const string Tyres500 = "tyres_500";
        public const string Barriers250 = "barriers_250";
        public const string Signs200 = "signs_200";
        public const string Bins50 = "bins_50";
        public const string Hay100 = "hay_100";
        public const string AmbulanceHit = "ambulance";
        public const string Strike = "strike";
        public const string Run100k = "run_100k";
        public const string Run250k = "run_250k";
        public const string ComboMaxed = "combo_max";
        public const string ComboHold30 = "combo_hold_30";
        public const string Bonus5 = "bonus_5";
        public const string PitContact = "pit_contact";
        public const string Pileup3 = "pileup_3";
        public const string RoofSpawn = "roof_spawn";
        public const string Reverse500m = "reverse_500m";

        public static readonly IReadOnlyList<AchievementDefinition> All = new[]
        {
            new AchievementDefinition(DriftFirst, AchievementVisibility.Visible, AchievementRarity.Common, AchievementIcon.DriftCar),
            new AchievementDefinition(Speed200, AchievementVisibility.Visible, AchievementRarity.Common, AchievementIcon.Speedometer),
            new AchievementDefinition(Speed300, AchievementVisibility.Hidden, AchievementRarity.Epic, AchievementIcon.Rocket),
            new AchievementDefinition(RevCuts1000, AchievementVisibility.Visible, AchievementRarity.Rare, AchievementIcon.Tachometer, 2000),
            new AchievementDefinition(Cones100, AchievementVisibility.Visible, AchievementRarity.Rare, AchievementIcon.Cone, 200),
            new AchievementDefinition(Kiss, AchievementVisibility.Hidden, AchievementRarity.Rare, AchievementIcon.Heart),
            new AchievementDefinition(Roof, AchievementVisibility.Hidden, AchievementRarity.Common, AchievementIcon.UpsideDown),
            new AchievementDefinition(Resets20, AchievementVisibility.Hidden, AchievementRarity.Common, AchievementIcon.Reset, 20),
            new AchievementDefinition(Air1, AchievementVisibility.Hidden, AchievementRarity.Rare, AchievementIcon.Wings),
            new AchievementDefinition(NightOwl, AchievementVisibility.Secret, AchievementRarity.Rare, AchievementIcon.Owl),
            new AchievementDefinition(Donut10, AchievementVisibility.Hidden, AchievementRarity.Epic, AchievementIcon.Donut),
            new AchievementDefinition(Drift90, AchievementVisibility.Hidden, AchievementRarity.Rare, AchievementIcon.Protractor),
            new AchievementDefinition(Reverse60, AchievementVisibility.Hidden, AchievementRarity.Rare, AchievementIcon.ReverseArrow),
            new AchievementDefinition(TBone, AchievementVisibility.Hidden, AchievementRarity.Epic, AchievementIcon.Cannonball),
            new AchievementDefinition(Wall, AchievementVisibility.Hidden, AchievementRarity.Rare, AchievementIcon.Bricks),
            new AchievementDefinition(Handbrake250, AchievementVisibility.Hidden, AchievementRarity.Epic, AchievementIcon.Handbrake),
            new AchievementDefinition(RevHold10, AchievementVisibility.Hidden, AchievementRarity.Rare, AchievementIcon.Speaker),
            new AchievementDefinition(Blinker2Km, AchievementVisibility.Hidden, AchievementRarity.Common, AchievementIcon.Blinker),
            new AchievementDefinition(Launch, AchievementVisibility.Visible, AchievementRarity.Rare, AchievementIcon.Lightning),
            new AchievementDefinition(BarrelRoll, AchievementVisibility.Secret, AchievementRarity.Legendary, AchievementIcon.Barrel),
            new AchievementDefinition(BurnoutSmokehouse, AchievementVisibility.Hidden, AchievementRarity.Rare, AchievementIcon.Smoke),
            new AchievementDefinition(BurnoutMarathon, AchievementVisibility.Hidden, AchievementRarity.Epic, AchievementIcon.Stopwatch),
            new AchievementDefinition(BurnoutCarousel, AchievementVisibility.Hidden, AchievementRarity.Rare, AchievementIcon.Carousel),
            new AchievementDefinition(BurnoutToDrift, AchievementVisibility.Hidden, AchievementRarity.Rare, AchievementIcon.SmokeDrift),
            new AchievementDefinition(Air3, AchievementVisibility.Hidden, AchievementRarity.Epic, AchievementIcon.Planet),
            new AchievementDefinition(AirTotal60, AchievementVisibility.Visible, AchievementRarity.Epic, AchievementIcon.Airplane, 120),
            new AchievementDefinition(RoofLanding, AchievementVisibility.Secret, AchievementRarity.Epic, AchievementIcon.Cat),
            new AchievementDefinition(Cones1000, AchievementVisibility.Visible, AchievementRarity.Epic, AchievementIcon.ConeStack, 2000),
            new AchievementDefinition(Tyres500, AchievementVisibility.Visible, AchievementRarity.Rare, AchievementIcon.TyreStack, 1000),
            new AchievementDefinition(Barriers250, AchievementVisibility.Visible, AchievementRarity.Epic, AchievementIcon.Barrier, 500),
            new AchievementDefinition(Signs200, AchievementVisibility.Hidden, AchievementRarity.Rare, AchievementIcon.RoadSign, 400),
            new AchievementDefinition(Bins50, AchievementVisibility.Hidden, AchievementRarity.Rare, AchievementIcon.Bin, 100),
            new AchievementDefinition(Hay100, AchievementVisibility.Hidden, AchievementRarity.Rare, AchievementIcon.HayBale, 200),
            new AchievementDefinition(AmbulanceHit, AchievementVisibility.Secret, AchievementRarity.Rare, AchievementIcon.Ambulance),
            new AchievementDefinition(Strike, AchievementVisibility.Hidden, AchievementRarity.Epic, AchievementIcon.BowlingPins),
            new AchievementDefinition(Run100k, AchievementVisibility.Visible, AchievementRarity.Epic, AchievementIcon.Medal),
            new AchievementDefinition(Run250k, AchievementVisibility.Hidden, AchievementRarity.Legendary, AchievementIcon.Trophy),
            new AchievementDefinition(ComboMaxed, AchievementVisibility.Hidden, AchievementRarity.Epic, AchievementIcon.ComboMax),
            new AchievementDefinition(ComboHold30, AchievementVisibility.Hidden, AchievementRarity.Legendary, AchievementIcon.Crown),
            new AchievementDefinition(Bonus5, AchievementVisibility.Hidden, AchievementRarity.Epic, AchievementIcon.BonusStack),
            new AchievementDefinition(PitContact, AchievementVisibility.Hidden, AchievementRarity.Rare, AchievementIcon.PitSign),
            new AchievementDefinition(Pileup3, AchievementVisibility.Hidden, AchievementRarity.Epic, AchievementIcon.Pileup),
            new AchievementDefinition(RoofSpawn, AchievementVisibility.Secret, AchievementRarity.Rare, AchievementIcon.Warning),
            new AchievementDefinition(Reverse500m, AchievementVisibility.Hidden, AchievementRarity.Rare, AchievementIcon.WrongWay),
        };

        public static AchievementDefinition? Find(string id)
        {
            foreach (var def in All)
                if (def.Id == id) return def;
            return null;
        }
    }
}
