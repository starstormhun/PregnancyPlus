﻿using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using KKAPI;
using KKAPI.Studio;
using KKAPI.Chara;

namespace KK_PregnancyPlus
{
    [BepInPlugin(GUID, GUID, Version)]
    [BepInDependency(KoikatuAPI.GUID, "1.12")]
    [BepInDependency("com.deathweasel.bepinex.uncensorselector", BepInDependency.DependencyFlags.SoftDependency)]
    #if KK
        [BepInDependency("KKPE", BepInDependency.DependencyFlags.SoftDependency)]
        [BepInDependency("KK_Pregnancy", BepInDependency.DependencyFlags.SoftDependency)]
    #elif HS2
        [BepInDependency("HS2PE", BepInDependency.DependencyFlags.SoftDependency)]
    #elif AI
        [BepInDependency("AIPE", BepInDependency.DependencyFlags.SoftDependency)]
    #endif
    public partial class PregnancyPlusPlugin : BaseUnityPlugin
    {
        public const string GUID = "KK_PregnancyPlus";
        public const string Version = "1.25";
        internal static new ManualLogSource Logger { get; private set; }

        #if DEBUG
            //Control all debug logging when running in debug mode
            internal static bool debugLog = true;
            internal static bool debugAllVerts = false;
            
        #else
            //Always leave these false here
            internal static bool debugLog = false;
            internal static bool debugAllVerts = false;
        #endif        

        //Used to hold the last non zero belly shape slider values that were applied to any character for Restore button
        public static PregnancyPlusData lastBellyState =  new PregnancyPlusData();        
        public static ErrorCodeController errorCodeCtrl;


        internal void Start()
        {
            Logger = base.Logger;    
            DebugTools.logger = Logger;
            errorCodeCtrl = new ErrorCodeController(Logger, debugLog);
            //Initilize the Bepinex F1 ConfigurationManager options
            PluginConfig();                    

            //Attach the mesh inflation logic to each character
            CharacterApi.RegisterExtraBehaviour<PregnancyPlusCharaController>(GUID);

            var hi = new Harmony(GUID);
            Hooks.InitHooks(hi);

            //Set up studio/malker GUI sliders
            PregnancyPlusGui.InitStudio(hi, this);
            PregnancyPlusGui.InitMaker(hi, this);
        }

    
        /// <summary>
        /// Triggers any charCustFunCtrl GUI components when blendshape GUI is opened in studio
        /// </summary>
        internal void OnGUI()
        {                
            if (!StudioAPI.InsideStudio) return;

            //Need to trigger all children GUI that should be active. 
            var handlers = CharacterApi.GetRegisteredBehaviour(GUID);
            if (handlers == null || handlers.Instances == null) return;

            #if !DEBUG  //Tired of the errors caused by ScriptEngine here
                foreach (PregnancyPlusCharaController charCustFunCtrl in handlers.Instances)
                {         
                    //Update any active gui windows
                    charCustFunCtrl.blendShapeGui.OnGUI(this);                                                    
                }
            #endif
        }
    
    }
}
