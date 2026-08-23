namespace LFSDriftBuddy.InSim
{
    /// <summary>
    /// InSim packet type identifiers (ISP_ enum from InSim.txt)
    /// </summary>
    public enum PacketType : byte
    {
        ISP_NONE = 0,
        ISP_ISI = 1,   // InSim Init
        ISP_VER = 2,   // Version info
        ISP_TINY = 3,   // Keep-alive / general small packet
        ISP_SMALL = 4,   // Small value packet
        ISP_STA = 5,   // State info
        ISP_SCH = 6,   // Single character
        ISP_SFP = 7,   // State flags pack
        ISP_SCC = 8,   // Set car camera
        ISP_CPP = 9,   // Camera position
        ISP_ISM = 10,  // InSim Multi
        ISP_MSO = 11,  // Message out
        ISP_III = 12,  // InSim info
        ISP_MST = 13,  // Message type (send message)
        ISP_MTC = 14,  // Message to connection
        ISP_MOD = 15,  // Set mode
        ISP_VTN = 16,  // Vote notify
        ISP_RST = 17,  // Race start
        ISP_NCN = 18,  // New connection
        ISP_CNL = 19,  // Connection left
        ISP_CPR = 20,  // Connection player rename
        ISP_NPL = 21,  // New player
        ISP_PLP = 22,  // Player pits
        ISP_PLL = 23,  // Player left
        ISP_LAP = 24,  // Lap info
        ISP_SPX = 25,  // Split X
        ISP_PIT = 26,  // Pit info
        ISP_PSF = 27,  // Pit stop finished
        ISP_PLA = 28,  // Pit lane action
        ISP_CCH = 29,  // Camera changed
        ISP_PEN = 30,  // Penalty
        ISP_TOC = 31,  // Take over car
        ISP_FLG = 32,  // Flag info
        ISP_PFL = 33,  // Player flags
        ISP_FIN = 34,  // Finished
        ISP_RES = 35,  // Result
        ISP_REO = 36,  // Reorder
        ISP_NLP = 37,  // Node and lap packet
        ISP_MCI = 38,  // Multi car info
        ISP_MSX = 39,  // Message extended
        ISP_MSL = 40,  // Message local
        ISP_CRS = 41,  // Car reset
        ISP_BFN = 42,  // Button function
        ISP_AXI = 43,  // Autocross info
        ISP_AXO = 44,  // Autocross object
        ISP_BTN = 45,  // Button (note: actual value is 45 in some versions)
        ISP_BTC = 46,  // Button click
        ISP_BTT = 47,  // Button type
        ISP_RIP = 48,  // Replay info packet
        ISP_SSH = 49,  // Screenshot
        ISP_CON = 50,  // Contact
        ISP_OBH = 51,  // Object hit
        ISP_HLV = 52,  // Hotlap validity
        ISP_PLC = 53,  // Player cars
        ISP_AXM = 54,  // Autocross multiple objects
        ISP_ACR = 55,  // Admin command result
        ISP_HCP = 56,  // Handicap
        ISP_NCI = 57,  // New connection info
        ISP_JRR = 58,  // Join/reject/respawn
        ISP_UCO = 59,  // User control object
        ISP_OCO = 60,  // Object control
        ISP_TTC = 61,  // Misc instr
        ISP_SLC = 62,  // Select car
        ISP_CSC = 63,  // Car state changed
        ISP_CIM = 64,  // Connection interface mode
        ISP_MAL = 65,  // Mods allowed list
        ISP_PLH = 66,  // Player handicap
        ISP_IPB = 67,  // IP ban
    }

    /// <summary>
    /// TINY sub-types
    /// </summary>
    public enum TinyType : byte
    {
        TINY_NONE = 0,   // Keep-alive
        TINY_VER = 1,   // Request version
        TINY_CLOSE = 2,   // Close InSim
        TINY_PING = 3,   // Ping
        TINY_REPLY = 4,   // Ping reply
        TINY_VTC = 5,   // Vote cancel
        TINY_SCP = 6,   // Send camera pos
        TINY_SST = 7,   // Send state info
        TINY_GTH = 8,   // Get time in hundredths
        TINY_MPE = 9,   // Multi player end
        TINY_ISM = 10,  // Request IS_ISM
        TINY_REN = 11,  // Race end (return to game setup)
        TINY_CLR = 12,  // All players cleared
        TINY_NCN = 13,  // Request all connections
        TINY_NPL = 14,  // Request all players
        TINY_RES = 15,  // Request all results
        TINY_NLP = 16,  // Request player position packets
        TINY_MCI = 17,  // Request multi car info packets
        TINY_REO = 18,  // Request a grid order
        TINY_RST = 19,  // Request race tracking
        TINY_AXI = 20,  // Request autocross info
        TINY_AXC = 21,  // Autocross cleared
        TINY_RIP = 22,  // Request replay info
        TINY_NCI = 23,  // Request all connections
        TINY_ALC = 24,  // Request allowed cars
        TINY_AXM = 25,  // Request autocross layout
        TINY_SLC = 26,  // Request selected car
        TINY_MAL = 27,  // Request mods allowed
        TINY_PLH = 28,  // Request player handicaps
        TINY_IPB = 29,  // Request IP bans
    }

    [System.Flags]
    public enum StateFlags : ushort
    {
        ISS_GAME = 1,             // in game (or MPR)
        ISS_REPLAY = 2,           // in single-player replay
        ISS_PAUSED = 4,
        ISS_SHIFTU = 8,           // free view (SHIFT+U)
        ISS_SHIFTU_FOLLOW = 16,   // free view follows car
        ISS_SHIFTU_NO_OPT = 32,   // free view player options
        ISS_SHOW_2D = 64,
        ISS_FRONT_END = 128,      // front end / main menu
        ISS_MULTI = 256,
        ISS_MPSPEEDUP = 512,
        ISS_WINDOWED = 1024,
        ISS_SOUND_MUTE = 2048,
        ISS_VIEW_OVERRIDE = 4096,
        ISS_VISIBLE = 8192,       // InSim buttons visible
        ISS_TEXT_ENTRY = 16384,
    }

    /// <summary>
    /// ISF flags for IS_ISI
    /// </summary>
    public enum ISFlags : ushort
    {
        ISF_RES_0 = 1,
        ISF_RES_1 = 2,
        ISF_LOCAL = 4,    // Use local messages
        ISF_MSO_COLS = 8,   // Keep colours in messages
        ISF_NLP = 16,   // Request periodic NLP packets
        ISF_MCI = 32,   // Request periodic MCI packets
        ISF_CON = 64,   // Receive connection packets
        ISF_OBH = 128,  // Receive object packets
        ISF_HLV = 256,  // Receive hotlap validity
        ISF_AXM_LOAD = 512, // Receive autocross load messages
        ISF_AXM_EDIT = 1024,// Receive autocross editor messages
        ISF_REQ_JOIN = 2048,// Receive join requests
    }

    /// <summary>
    /// Button styles for IS_BTN
    /// </summary>
    public enum ButtonStyle : byte
    {
        ISB_C1 = 1,    // Click 1
        ISB_C2 = 2,    // Click 2
        ISB_C4 = 4,    // Click 4
        ISB_CLICK = 7,    // Any click
        ISB_LIGHT = 8,    // Light background
        ISB_DARK = 16,   // Dark background
        ISB_LEFT = 32,   // Align left
        ISB_RIGHT = 64,   // Align right
    }
}