using UnityEngine.Experimental.GlobalIllumination;

namespace Zombie1111_uDestruction
{
    /*
        #################Optional Scripting Define Symbols (ProjectSettings>Player>OtherSettings)################
        #########################################################################################################

        FRAC_NO_WARNINGS //If defined, no warnings will be logged to console + minor performance boost
        FRAC_NO_VERIFICATION //If defined, no verify saving before fracturing and no notices
    */

    public static class FracGlobalSettings
    {
        //When fracturing more complex meshes it can some times fail. This is how many times it will try to fracture a mesh before giving up and throwing an error.
        public const int maxFractureAttempts = 20;

        //Its highly recommended that the objects in your scene has a neutral scale/size. If for whatever reason you cant have that you can try to change this value
        public const float worldScale = 1.0f;

        //if true, fixedUpdate will run rougly as often as Update but no less than it did on awake. Since 90% of the destruction system runs on fixedUpdate,
        //if fixedUpdate = 50fps and Update = 200fps you may get a noticable fps drop every 4 frames. This will try to prevent this by also increasing fixedUpdate if update is > 50fps
        public const bool syncFixedTimestepWithFps = false;

        //If true, all enabled rigidbodies will automatically be registered by the globalHandler when a scene is loaded
        public const bool addAllActiveRigidbodiesOnLoad = true;

        //The minimum force a impact must cause for it to cause any destruction 
        public const float minimumImpactForce = 5.0f;

        //The minimum velocity a rigidbody must have for it to cause any destruction
        public const float minimumImpactVelocity = 0.1f;

        //If == 1.0f and a collider has a bouncyness of 1.0f the collider can not cause any destruction since all its energy gets "consumed" by the bounce
        public const float bouncynessEnergyConsumption = 1.0f;

        //If true and the impact has >= 3 contact points the contact points will be threated as a triangel and the normal of that triangel will be used as impact normal
        public const bool canGetImpactNormalFromPlane = false;

        //A higher value will cause the impact angle/normal to have less affect on the damage done by the impact, a value of 1 will cause it to have 0 affect
        public const float normalInfluenceReduction = 0.5f;

        //Same as above but overrides it for impacts caused by the destructable object, example if the object falls and hit the ground 
        public const float normalInfluenceReductionSelf = 0.0f;

        //If fracture.MainPhyiscsType == OverlappingIsKinematic, the overlapping colliders will always be calculated when fracturing.
        //But it may be useful to also recalculate it when loading a fracture, recalculating will increase loading time!
        //0 == never recalculate, 1 == only recalculate for prefab instances, 2 == always recalculate
        public const byte recalculateKinematicPartsOnLoad = 1;

        //How much weaker a material can be depending on the difference between force direction and structure direction.
        public const float transDirInfluenceReduction = 1.0f;

        //How many neighbours any part can have at most. Neighbours uses fixed buffers so it cant be dynamic. Higher = more memory, keep it as low as possible and look for warnings in console if its too low
        public const byte maxPartNeighbourCount = 32;

        //How many deformation bones each vertex can have. Higher = more memory, keep it as low as possible and look for warnings in console if its too low
        //maxDeformationBones IN UltimateFracture/Resources/ComputeGlobalSettings.cginc MUST ALSO BE THE SAME!!!
        public const byte maxDeformationBones = 64;

        //If collision deformation is enabled, it has to sync all colliders with the deformed mesh.
        //Modifying a collider aint very fast, this limits how many colliders each destructable object can sync per frame
        public const byte maxColliderUpdatesPerFrame = 2;

        //How many parts a parent must have for it to be created when destoying stuff, used to prevent hundreds of tiny parents from potentially being created when destroying stuff
        public const byte minParentPartCount = 3;
    }
}
