namespace LFSDriftTools
{
    public sealed class UserIntent
    {
        public bool Wanted { get; set; }

        public UserIntent(bool initial)
        {
            Wanted = initial;
        }

        public void Capture(bool current)
        {
            Wanted = current;
        }

        public bool Resolve(bool active, bool current) => active ? current : Wanted;

        public void Apply(MacToggleSwitch? toggle, bool available)
        {
            if (toggle == null) return;

            toggle.Enabled = available;
            toggle.SetCheckedSilent(available && Wanted);
        }
    }
}