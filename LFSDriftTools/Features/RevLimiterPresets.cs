using System.Collections.Generic;

namespace LFSDriftTools
{
    public static class RevLimiterPresets
    {
        private const int DefaultCutMs = 35;

        private static readonly (string Car, int MaxRpm, bool Unsupported)[] Entries =
        {
            ("C4FE2800", 8434, false),
            ("A4167B00", 6837, false),
            ("2FC26300", 6931, false),
            ("7E74B900", 7935, false),
            ("F4A67500", 9423, false),
            ("F62F5800", 6436, false),
            ("3736B700", 9221, false),
            ("5EA53800", 5935, false),
            ("05C83A00", 9143, false),
            ("C2FB7800", 7742, false),
            ("94388C00", 7936, false),
            ("71289E00", 6679, false),
            ("AABEAA00", 6934, false),
            ("5CCFC400", 7435, false),
            ("2685EA00", 8180, false),
            ("926E7E00", 7425, false),
            ("UF1", 6933, false),
            ("XFG", 7929, false),
            ("XRG", 6930, false),
            ("LX4", 8924, false),
            ("LX6", 8925, false),
            ("RB4", 7430, false),
            ("FXO", 7432, false),
            ("XRT", 7431, false),
            ("RAC", 6439, false),
            ("FZ5", 7921, false),
            ("UFR", 8928, false),
            ("XFR", 7929, false),
            ("FXR", 7442, false),
            ("XRR", 7442, false),
            ("FZR", 8425, false),
            ("MRT", 12871, false),
            ("FBM", 9129, false),
            ("FOX", 7431, false),
            ("FO8", 9426, false),
            ("BF1", 19862, false),
            ("883A2600", 8913, false),
            ("A1B94000", 7527, false),
            ("4A41DC00", 8956, false),
            ("99D95F00", 8619, false),
            ("A4064D00", 7530, false),
            ("762ECF00", 4438, false),
            ("73D55600", 6737, false),
            ("FEF15600", 20000, true),
            ("8F5BE900", 5922, false),
            ("AB0AFC00", 3845, false),
            ("354AE700", 4644, false),
            ("ACE67500", 7428, false),
            ("1235D600", 7969, false),
            ("915CF100", 9425, false),
            ("06C8D300", 7416, false),
            ("BEB83E00", 7231, false),
            ("C4C16F00", 7338, false),
            ("20D1C000", 7175, false),
            ("1ED1D500", 8162, false),
            ("4E761500", 7967, false),
            ("07BCE200", 14388, false),
            ("F22DB900", 6929, false),
            ("3A26D200", 8913, false),
            ("55399900", 12910, false),
            ("C6856C00", 8424, false),
            ("6FA6CC00", 3945, false),
            ("A3332F00", 4945, false),
            ("F06A8400", 7531, false),
            ("B4506400", 8235, false),
            ("99D73500", 7728, false),
            ("FED39E00", 8429, false),
            ("2D6E8000", 8784, false),
            ("B4DFA600", 7120, false),
            ("BA7F9E00", 8927, false),
            ("85C4A400", 9424, false),
            ("4E722300", 11109, false),
            ("2A394200", 8324, false),
            ("5F996C00", 8930, false),
            ("4FCF1B00", 11412, false),
            ("1E24C500", 13729, false),
            ("06946300", 7416, false),
            ("57848A00", 9017, false),
            ("508A8900", 9172, false),
            ("6A791E00", 11915, false),
            ("EFA4F100", 7437, false),
            ("F5B56F00", 9206, false),
            ("81891F00", 8430, false),
            ("66A49C00", 8421, false),
            ("2626D100", 6929, false),
            ("14DC3B00", 7950, false),
            ("98611000", 2244, false),
            ("70116200", 7934, false),
            ("B1744D00", 8324, false),
        };

        public static Dictionary<string, VehicleRevSettings> Create()
        {
            var result = new Dictionary<string, VehicleRevSettings>();
            foreach (var entry in Entries)
            {
                result[entry.Car] = new VehicleRevSettings
                {
                    MaxRpm = entry.MaxRpm,
                    CutMs = DefaultCutMs,
                    Unsupported = entry.Unsupported,
                };
            }
            return result;
        }
    }
}
