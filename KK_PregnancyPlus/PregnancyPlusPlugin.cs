﻿using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using KKABMX.Core;
using KKAPI;
using KKAPI.Chara;
using KKAPI.MainGame;

namespace KK_PregnancyPlus
{
    [BepInPlugin(GUID, GUID, Version)]
    [BepInDependency(KoikatuAPI.GUID, KoikatuAPI.VersionConst)]
    public partial class PregnancyPlusPlugin : BaseUnityPlugin
    {
        public const string GUID = "KK_PregnancyPlus";
        public const string Version = "0.1";

        internal static new ManualLogSource Logger { get; private set; }

        private void Start()
        {
            Logger = base.Logger;
            
            var _GUID = "KK_Pregnancy";//Allows us to pull KK_pregnancy data values with GetExtendedData()
            CharacterApi.RegisterExtraBehaviour<PregnancyPlusCharaController>(_GUID);

            var hi = new Harmony(GUID);
            Hooks.InitHooks(hi);
            PregnancyPlusGui.Init(hi, this);
        }
    }
}
