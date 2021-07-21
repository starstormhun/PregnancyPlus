﻿using KKAPI;
using KKAPI.Chara;
using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KKAPI.Maker;
using KKAPI.Studio;
#if KK
    using KKAPI.MainGame;
#elif HS2
    using AIChara;
#elif AI
    using KKAPI.MainGame;
    using AIChara;
#endif

namespace KK_PregnancyPlus
{

    //This partial class contains the characters properties, fields, and overrides
    public partial class PregnancyPlusCharaController: CharaCustomFunctionController
    {        

        internal bool initialized = false;//Prevent some actions from happening before character data loads   

        public BellyInfo bellyInfo;
        public string charaFileName = null;
        public bool lastVisibleState = false;//Track last mesh render state, to determine when to re-apply preg+ shape in main game
        public bool uncensorChanged = false;
        public bool isReloading = false;//While character.Reload() is processing prevent other MeshInflate() instances
        internal bool ignoreNextUncensorHook = false;//When we want to ignore a single uncensor hook event
        public PregnancyPlusBlendShapeGui blendShapeGui = new PregnancyPlusBlendShapeGui();

        //Holds the user entered slider values
        public PregnancyPlusData infConfig = new PregnancyPlusData();
        internal PregnancyPlusData infConfigHistory = new PregnancyPlusData();        


        //Keeps track of all belly verticie data for preg+, the dict is indexed by the (meshRenderer.name + the vertex count) to make the mesh indexes unique
        public Dictionary<string, MeshData> md = new Dictionary<string, MeshData>();
        public List<string> ignoreMeshList = new List<string>();//List of mesh names/keys to ignore since they dont have belly verts



        //For fetching uncensor body guid data (bugfix for uncensor body vertex positions)
        public const string UncensorCOMName = "com.deathweasel.bepinex.uncensorselector";
        public const string DefaultBodyFemaleGUID = "Default.Body.Female";
        public const string DefaultBodyMaleGUID = "Default.Body.Male";

        #if KK //key that allows us to pull KK_pregnancy data values
            public const string KK_PregnancyPluginName = "KK_Pregnancy";
        #elif AI
            public const string KK_PregnancyPluginName = "AI_Pregnancy";
        #elif HS2
            public const string KK_PregnancyPluginName = "";
        #endif

        internal Guid debounceGuid;//Track multiple events with a debounce based on these id's

        //Determine body mesh name based on sex (0 is male)
        public string BodyMeshName {
            #if KK
                get { return ChaControl.sex == 0 ?  "o_body_a" : "o_body_a"; }
            #elif HS2 || AI
                get { return ChaControl.sex == 0 ?  "o_body_cm" : "o_body_cf"; }
            #endif            
        }

        //Used to multithread some complex tasks.  Cant use fancy new unity threading methods, because of KK's old unity version
        public Threading threading = new Threading();


#region overrides

        protected override void OnCardBeingSaved(GameMode currentGameMode)
        {
            //only allow saving card inside maker or studio
            if (!StudioAPI.InsideStudio && !MakerAPI.InsideMaker) return;
            if (PregnancyPlusPlugin.DebugLog.Value)  PregnancyPlusPlugin.Logger.LogInfo($"+= $OnCardBeingSaved ");

            CaptureNewBlendshapeWeights();            
            SetExtendedData(infConfig.Save());
        }


        protected override void Start() 
        {  
            uncensorChanged = false;//reset value to signify its not a change made manually by the user
            
            //Character card name 
            charaFileName = ChaFileControl.parameter.fullname;        
            if (PregnancyPlusPlugin.DebugLog.Value)  PregnancyPlusPlugin.Logger.LogInfo($" ");
            if (PregnancyPlusPlugin.DebugLog.Value)  PregnancyPlusPlugin.Logger.LogInfo($"+= $Start {charaFileName}");
            ReadAndSetCardData();      

            #if KK || AI

                //When HScene starts, pre compute inflated size blendshape
                #if !KKS //TODO add gameAPI later when KKS releases fully
                    GameAPI.StartH += (object sender, EventArgs e) => 
                    { 
                        if (PregnancyPlusPlugin.DebugLog.Value)  PregnancyPlusPlugin.Logger.LogInfo($"+= $StartH {charaFileName}");
                        //Trigger inflation at current size to create any Preg+ blendshapes that may be used.  Kind of like like pre processing.
                        MeshInflate(infConfig.inflationSize, "GameAPI.StartH", new MeshInflateFlags(this, _bypassWhen0: true));
                    };

                    //When HScene ends, clear any inflation data
                    GameAPI.EndH += (object sender, EventArgs e) => 
                    { 
                        if (PregnancyPlusPlugin.DebugLog.Value)  PregnancyPlusPlugin.Logger.LogInfo($"+= $EndH {charaFileName}");
                        ClearInflationStuff(fullReset: true);
                    };
                #endif
         
            #endif

            // CharacterApi.CharacterReloaded += (object sender, CharaReloadEventArgs e) =>  
            // {  
            //     if (e.ReloadedCharacter == null || e.ReloadedCharacter.name != ChaControl.name) return;            
            //     if (PregnancyPlusPlugin.DebugLog.Value)  PregnancyPlusPlugin.Logger.LogInfo($"+= OnCharacterReloaded ");
            // };

            base.Start();
        }        


        //The HS2 / AI way to detect clothing change
        protected override void OnCoordinateBeingLoaded(ChaFileCoordinate coordinate) 
        {
            if (PregnancyPlusPlugin.DebugLog.Value)  PregnancyPlusPlugin.Logger.LogInfo($"+= $OnCoordinateBeingLoaded {coordinate.coordinateName}");
            OnCoordinateLoaded();

            base.OnCoordinateBeingLoaded(coordinate);
        }
        

        protected override void OnReload(GameMode currentGameMode)
        {
            if (PregnancyPlusPlugin.DebugLog.Value)  PregnancyPlusPlugin.Logger.LogInfo($" ");
            if (PregnancyPlusPlugin.DebugLog.Value)  PregnancyPlusPlugin.Logger.LogInfo($"+= $OnReload {currentGameMode}");
            isReloading = true;
            lastVisibleState = false;            

            ClearOnReload();
            #if AI || HS2
                //Fix for the way AI injects new characters in Main Game by copying existing character first.  This will remove the old blendshapes.
                ScrubTempBlendShapes();
            #endif

            //(no longer used) Check for swapping out character Game Object with new character
            IsNewChar(ChaFileControl);
            charaFileName = ChaFileControl.parameter.fullname;

            ReadAndSetCardData();

            // When changing a character (swapping in place) in studio, carry over belly sliders/blendshapes
            //TODO there has to be a better way to detect swapping characters
            if (StudioAPI.InsideStudio && (infConfigHistory.HasAnyValue() || infConfigHistory.HasBlendShape()))
            {
                if (PregnancyPlusPlugin.DebugLog.Value)  PregnancyPlusPlugin.Logger.LogInfo($" -Character changed in place, preserving belly shape");
                infConfig = infConfigHistory;
                if (PregnancyPlusPlugin.DebugLog.Value)  PregnancyPlusPlugin.Logger.LogInfo($" Updating Card Data > {infConfig.ValuesToString()}");
            }

            //If the uncensor changed just before this Reload() fired, then is was probably a character swap.
            if (uncensorChanged)
            {
                uncensorChanged = false;
                
                //When in maker or studio we want to reset inflation values when uncensor changes to reset clothes
                if (StudioAPI.InsideStudio || MakerAPI.InsideMaker) ResetInflation();        
                //Load any saved blendshapes from card, and can trigger uncensor change when necessary
                //Give any existing uncensor changes time to process first
                StartCoroutine(ILoadBlendshapes(0.1f, checkUncensor: true));
            }   
            else 
            {
                //Load any saved blendshapes from card, or trigger uncensor change when necessary
                StartCoroutine(ILoadBlendshapes(0f));
            }         

            StartCoroutine(ReloadStoryInflation(0.5f, "Reload-story"));     
            StartCoroutine(ReloadStudioMakerInflation(1.5f, reMeasure: true, "Reload"));  //Give time for character to load, and settle  
        }


        protected override void Update()
        {
            WatchForUserKeyPress();
            ComputeInflationChange();

            //just for debugging belly during animations, very compute heavy for Update()
            if (PregnancyPlusPlugin.DebugAnimations.Value)
            {
                if (Time.frameCount % 60 == 0) MeshInflate(new MeshInflateFlags(this, _checkForNewMesh: true, _freshStart: true, _reMeasure: true), "Update");
            }

            //Execute thread results in main thread, when existing threads are done processing
            threading.WatchAndExecuteThreadResults();
        }


        protected override void OnDestroy() 
        {
            if (PregnancyPlusPlugin.DebugLog.Value)  PregnancyPlusPlugin.Logger.LogInfo($"+= $OnDestroy {charaFileName}"); 
        }
        

#endregion overrides


    }
}


