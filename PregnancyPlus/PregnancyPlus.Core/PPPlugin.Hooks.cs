﻿using HarmonyLib;
using KKAPI.Maker;
using KKAPI.Studio;
using KKAPI.Chara;
#if AI || HS2
    using AIChara;
#elif KK
    using KKAPI.MainGame;
#endif


namespace KK_PregnancyPlus
{
    public partial class PregnancyPlusPlugin
    {
        /// <summary>
        /// Provides access to methods for getting and setting clothes state changes to a specific CharCustomFunctionController.
        /// </summary>
        /// <param name="chaControl"></param>
        /// <returns>KKAPI character controller</returns>
        public static PregnancyPlusCharaController GetCharaController(ChaControl chaControl) => (chaControl == null)
            ? null 
            : chaControl.gameObject.GetComponent<PregnancyPlusCharaController>();



        private static class Hooks
        {
            public static void InitHooks(Harmony harmonyInstance)
            {
                harmonyInstance.PatchAll(typeof(Hooks));
            }
			
			#if HS2

                //HS2 inflation trigger logic, copied from KK_Pregnancy
                [HarmonyPrefix]
                [HarmonyPatch(typeof(Sonyu), "Proc", typeof(int), typeof(HScene.AnimationListInfo))]
                public static void Sonyu_Proc(Sonyu __instance)
                {                    
                    //Get current user button click type
                    var ctrlFlag = Traverse.Create(__instance).Field("ctrlFlag").GetValue<HSceneFlagCtrl>();                                    
                    DetermineInflationState(ctrlFlag);                   
                }


                [HarmonyPrefix]
                [HarmonyPatch(typeof(Houshi), "Proc", typeof(int), typeof(HScene.AnimationListInfo))]
                public static void Houshi_Proc(Houshi __instance)
                {                    
                    //Get current user button click type
                    var ctrlFlag = Traverse.Create(__instance).Field("ctrlFlag").GetValue<HSceneFlagCtrl>(); 
                    DetermineInflationState(ctrlFlag);                                   
                }

                //When user clicks finish button, set the inflation based on the button clicked
                private static void DetermineInflationState(HSceneFlagCtrl ctrlFlag)
                {
                    //swallow clicked
                    if (ctrlFlag.click == HSceneFlagCtrl.ClickKind.FinishInSide 
                        || ctrlFlag.click == HSceneFlagCtrl.ClickKind.FinishSame  
                        || ctrlFlag.click == HSceneFlagCtrl.ClickKind.FinishDrink ) 
                    {
                        TriggerInflation();                   
                    }
                    //spit clicked
                    else if (ctrlFlag.click == HSceneFlagCtrl.ClickKind.FinishOutSide 
                        || ctrlFlag.click == HSceneFlagCtrl.ClickKind.FinishVomit) 
                    {
                        TriggerInflation();
                    }                        
                }

                /// <summary>
                /// Add for cumflation effect in HS2 only.  (Too lazy to implement deflation logic right now)
                /// </summary>
                private static void TriggerInflation(bool deflate = false)
                {
                    if (StudioAPI.InsideStudio || MakerAPI.InsideMaker) return;//Don't allow in studio/maker
                    if (!StoryMode.Value || !AllowCumflation.Value) return;
                                        
                    if (PregnancyPlusPlugin.DebugLog.Value) PregnancyPlusPlugin.Logger.LogInfo($" HSceneFlagCtrl_AddOrgasm_HS2");
                    var handlers = CharacterApi.GetRegisteredBehaviour(GUID);

                    //This will probably inflate all characters in scene, but I don't care
                    foreach (PregnancyPlusCharaController charCustFunCtrl in handlers.Instances)
                    {
                        //Trigger inflation
                        charCustFunCtrl.HS2Inflation(deflate);
                    }                    
                }
            #endif


            /// <summary>
            /// Trigger the ClothesStateChangeEvent for toggling on and off a clothing item
            /// </summary>
            [HarmonyPostfix, HarmonyPatch(typeof(ChaControl), nameof(ChaControl.SetClothesState))]
            private static void ChaControl_SetClothesState(ChaControl __instance, int clothesKind)
            {
                //Ignore gloves, shoes, socks
                if (IsIgnoredClothing(clothesKind)) return;

                var controller = GetCharaController(__instance);
                if (controller == null) return;
            
                //Send event to the CustomCharaFunctionController that the clothes were changed on
                controller.ClothesStateChangeEvent(__instance.chaID, clothesKind);                                
            }


            /// <summary>
            /// Trigger the ClothesStateChangeEvent when changing custom outfits in maker
            /// </summary>
            [HarmonyPostfix, HarmonyPatch(typeof(ChaControl), nameof(ChaControl.ChangeCustomClothes))]
            private static void ChaControl_ChangeCustomClothes(ChaControl __instance, int kind)
            {

                //Ignore gloves, shoes, socks
                if (IsIgnoredClothing(kind)) return;

                if (MakerAPI.InsideAndLoaded)
                {
                    var controller = GetCharaController(__instance);
                    if (controller == null) return;
                
                    //Send event to the CustomCharaFunctionController that the clothes were changed on
                    controller.ClothesStateChangeEvent(__instance.chaID, kind);  
                }
            }

            #if HS2 || AI
                /// <summary>
                /// When HS2WearCustom changes clothing (catches clothes change that the above does not)
                /// </summary>
                [HarmonyPostfix, HarmonyPatch(typeof(ChaControl), nameof(ChaControl.ChangeClothesAsync), typeof(int), typeof(int), typeof(bool), typeof(bool))]
                private static void ChaControl_ChangeClothesAsync(ChaControl __instance, int kind, int id, bool forceChange, bool asyncFlags)
                {
                    //Dont ignore any clothes types here, since they can come with additional uncensor mesh as well (like Squeeze Socks)
                    if (StudioAPI.InsideStudio || MakerAPI.InsideAndLoaded)
                    {
                        var controller = GetCharaController(__instance);
                        if (controller == null) return;
                    
                        //Send event to the CustomCharaFunctionController that the clothes were changed on
                        controller.ClothesStateChangeEvent(__instance.chaID, kind);  
                    }
                }
            #endif


            #if KK
                /// <summary>
                /// When a character becomes visible let preg+ controller know, in main game mode only
                /// </summary>
                [HarmonyPostfix]
                [HarmonyPatch(typeof(ChaControl), "UpdateForce")]
                private static void VisibilityStateEvent(ChaControl __instance)
                {
                    //Only continue in main game mode
                    if (!__instance.loadEnd || !PregnancyPlusPlugin.StoryMode.Value || StudioAPI.InsideStudio || MakerAPI.InsideAndLoaded)
                    {
                        return;
                    }

                    bool newState = __instance.rendBody.isVisible;

                    //Send current visible state to each character's preg+ controller                    
                    var controller = GetCharaController(__instance);
                    if (controller == null) return;

                    controller.CheckVisibilityState(newState);
                }

            #endif


            /// <summary>
            /// Ignore gloves, socks, and shoes since they dont affect the belly area
            /// </summary>
            internal static bool IsIgnoredClothing(int clothesKind) 
            {
                #if KK
                    return (clothesKind == (int)ChaFileDefine.ClothesKind.gloves || clothesKind == (int)ChaFileDefine.ClothesKind.socks || clothesKind == (int)ChaFileDefine.ClothesKind.shoes_inner || clothesKind == (int)ChaFileDefine.ClothesKind.shoes_outer);
                #elif HS2 || AI
                    return (clothesKind == (int)ChaFileDefine.ClothesKind.gloves || clothesKind == (int)ChaFileDefine.ClothesKind.socks || clothesKind == (int)ChaFileDefine.ClothesKind.shoes);
                #endif
            }

        }
    }
}
