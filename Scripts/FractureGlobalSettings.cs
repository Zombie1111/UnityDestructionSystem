using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Zombie1111_uDestruction
{
    /*
        #################Optional Scripting Define Symbols (ProjectSettings>Player>OtherSettings)################
        #########################################################################################################

        FRAC_NO_WARNINGS //If defined, no warnings will be logged to console
        FRAC_NO_VERIFICATION //If defined, no verify saving before fracturing and no notices
    */

    public static class FracGlobalSettings
    {
        public const int maxFractureAttempts = 20;
        public const float worldScale = 1.0f;
        public const bool syncFixedTimestepWithFps = false;
        public const bool addAllActiveRigidbodiesOnLoad = true;
    }
}
