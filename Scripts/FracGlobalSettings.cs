namespace zombDestruction
{
    /*
        #################Optional Scripting Define Symbols (ProjectSettings>Player>OtherSettings)################
        #########################################################################################################

        FRAC_NO_WARNINGS //If defined, no warnings will be logged to console + minor performance boost
        FRAC_NO_VERIFICATION //If defined, no verify saving before fracturing and no notices
        FRAC_NO_BURST //If defined, burst wont be used in zombDestruction namespace
        FRAC_NO_VERTEXCOLORSUPPORT //If defined, vertex colors wont be supported, also change SUPPORTVERTEXCOLORS in ComputeGlobalSettings.cginc
        FRAC_NO_VERTEXCOLORSAVESTATESUPPORT //If defined, VERTEXCOLORS wont be loaded/saved by fracSaveStates
    */

    public static class FracGlobalSettings
    {
        //When fracturing complex meshes it can some times fail. This is how many times it will try to fracture a mesh before giving up and throwing an error.
        public const int maxFractureAttempts = 20;

        //Its highly recommended that the objects in your scene has a neutral scale/size. If for whatever reason you cant have that you can try to change this value
        public const float worldScale = 1.0f;

        //if > 0.0f, fixedUpdate will run rougly as often as Update but no less than it did on awake. Since 90% of the destruction system runs on fixedUpdate,
        //if fixedUpdate = 50fps and Update = 200fps you may get a noticable fps drop every 4 frames.
        //This will try to prevent this by also increasing fixedUpdate if update is > orginal fixedUpdate
        public const float minDynamicFixedTimeStep = 0.02f;
        public const float maxDynamicFixedTimeStep = 0.007f;//Inverse, I thought about fps not ms when making it

        //The minimum force a impact must cause for it to cause any destruction 
        public const float minimumImpactForce = 5.0f;

        //The minimum velocity a rigidbody must have for it to cause any destruction
        public const float minimumImpactVelocity = 2.0f;

        //If == 1.0f and a collider has a bouncyness of 1.0f the collider can not cause any destruction since all its energy gets "consumed" by the bounce
        public const float bouncynessEnergyConsumption = 1.0f;

        //If true and the impact has >= 3 contact points the contact points will be threated as a triangel and the normal of that triangel will be used as impact normal
        public const bool canGetImpactNormalFromPlane = false;

        //A higher value will cause the impact angle/normal to have less affect on the damage done by the impact, a value of 1 will cause it to have 0 affect
        public const float normalInfluenceReduction = 0.5f;

        //Same as above but overrides it for impacts caused by itself, example if the object falls and hit the ground 
        public const float normalInfluenceReductionSelf = 0.5f;

        //If fracture.MainPhyiscsType == OverlappingIsKinematic, the overlapping colliders will always be calculated when fracturing.
        //But it may be useful to also recalculate it when loading a fracture, recalculating will increase loading time!
        //0 == never recalculate, 1 == only recalculate for prefab instances, 2 == always recalculate
        public const byte recalculateKinematicPartsOnLoad = 1;

        //How much weaker a material can be depending on the difference between force direction and structure direction
        public const float transDirInfluenceReduction = 1.0f;

        //How many neighbours any part can have at most. Neighbours uses fixed buffers so it cant be dynamic.
        //Higher = more memory, keep it as low as possible and look for warnings in console if its too low
        public const byte maxPartNeighbourCount = 32;

        //If deformation is enabled, it has to sync all colliders with the deformed mesh.
        //Modifying a collider aint very fast, this limits how many colliders each destructable object can sync per frame
        public const byte maxColliderUpdatesPerFrame = 2;

        //For performance reasons it only syncs a collider if its mesh has been deformed.
        //If false it may sync too few colliders, if true it may sync too many colliders
        public const bool sensitiveColliderSync = false;

        //How many parts a parent must have for it to be created when destoying stuff,
        //used to prevent hundreds of tiny parents from potentially being created when destroying stuff
        public const byte minParentPartCount = 3;

        //If false, parts that are kinematic wont break from impacts (Kinematic parts are usually parts that are overlapping with other geometry if
        //MainPhysicsType == OverlappingIsKinematic)
        public const bool kinematicPartsCanBreak = true;

        //The min&max mass a rigidbody created by the destruction system can have,
        public const float rbMinMass = 0.01f;
        public const float rbMaxMass = 10.0f;

        //If true, it will try to prevent a mesh from being deformed through another object (Comes at a performance cost and not 100% accurate)
        public const bool doDeformationCollision = true;

        //If true, it will try to prevent kinematic parts from being deformed (Not 100% accurate)
        public const bool preventKinematicDeformation = false;

        //If true, it will remove destroyed rigidbodies added to the globalHandler on sceneLoaded and onDestroy destructionBody
        //However null exceptions should never happen even if null rigidbodies does exist,
        //is the performance cost of removing rigidbodies worth the minor risk of null exception?
        public const bool canAutomaticallyRemoveAddedRigidbodies = true;

        //A multiplier on the velocity a part that breaks actually gets
        public const float partBreakVelocityMultiplier = 1.0f;

        //When guessing force needed to break part, the result is multiplied with this value.
        public const float guessStrenghtMultiplier = 0.9f;

        //When guessing how much force part can apply to X, the result is multiplied with this value.
        public const float guessStrenghtApplyMultiplier = 1.1f;

        //A lot of stuff is lerped by this value to smooth movement
        public const float smoothLerp = 0.45f;

        //When creating parent template, these component types will be included in the template
        //(Rigidbody&Transform&Collider&Renderer&MeshFilter is always included and should never be in this array)
        public static string[] componentTypeNamesToInclude = new string[] { }; //Example if you wanna include a script called ObjectChildData: { "ObjectChildData" }

        //If true, the mass of all parts that has the same rigidbody will be added togehter when computing combined mass
        //Otherwise its the total mass of all parts that has the same parent
        public const bool rbMassIsPerRigidbody = true;
        public const bool desMassIsPerRigidbody = false;
    }
}
