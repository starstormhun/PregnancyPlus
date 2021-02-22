﻿using KKAPI;
using KKAPI.Chara;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using KKAPI.Studio;
using KKAPI.Maker;

using UniRx;
#if HS2 || AI
using AIChara;
#endif

namespace KK_PregnancyPlus
{

    //This partial class contains all the less critical mesh inflation methods
    public partial class PregnancyPlusCharaController: CharaCustomFunctionController
    {

        //Used to determine belly scale direction
        public enum BellyDir
        {
            x,y,z
        }

        public class BellyInfo 
        {
            public float WaistWidth;
            public float ScaledWaistWidth
            {
                get { return WaistWidth * TotalCharScale.x; }
            }
            
            public float WaistHeight;
            public float ScaledWaistHeight
            {
                get { return WaistHeight * TotalCharScale.y; }
            }

            public float WaistThick;
            public float ScaledWaistThick
            {
                get { return WaistThick * TotalCharScale.z; }
            }

            public Vector3 CharacterScale;//ChaControl.transform scale (set by the Axis scale control)
            public Vector3 BodyTopScale;//BodyTop bone scale
            public Vector3 NHeightScale;//n_height bone scale
            public Vector3 TotalCharScale
            {
                //Multiply x*x, y*y etc to get the toal character scale
                get { return new Vector3(BodyTopScale.x * CharacterScale.x, BodyTopScale.y * CharacterScale.y, BodyTopScale.z * CharacterScale.z); }
            }

            public float SphereRadius;
            public float OriginalSphereRadius;
            public float CurrentMultiplier;

            //From char z=0 position
            public float ZLimit
            {
                //Get the distance from center -> spine, where the belly is allowed to wrap around to (total distance from 0 to back bone /some scale that looks good)
                get { return ScaledWaistThick/2f; }
            }

            public float BellyToBreastDist;//Belly button to breast distance
            public float ScaledBellyToBreastDist
            {
                get { return BellyToBreastDist * TotalCharScale.y; }
            }

            //From char belly button to breast distance
            public float YLimit
            {
                //Get the distance from center -> ribs, with scale applied
                get { return ScaledBellyToBreastDist; }
            }

            public float BellyButtonHeight;//Foot to belly button height
            public bool MeshRootDidMove = false;//Keep track when we move the meshroot position for certain clothing positional calculations

            
            public bool IsInitialized 
            {
                get { return WaistWidth > 0 && WaistHeight > 0; }
            }

            //Get the sphere radius asjusted by the characters scale
            public float ScaledRadius(BellyDir dir)
            {
                if (dir == BellyDir.x) return SphereRadius/TotalCharScale.x;
                if (dir == BellyDir.y) return SphereRadius/TotalCharScale.y;
                if (dir == BellyDir.z) return SphereRadius/TotalCharScale.z;
                return -1;
            }

            public float ScaledOrigRadius(BellyDir dir)
            {
                if (dir == BellyDir.x) return OriginalSphereRadius/TotalCharScale.x;
                if (dir == BellyDir.y) return OriginalSphereRadius/TotalCharScale.y;
                if (dir == BellyDir.z) return OriginalSphereRadius/TotalCharScale.z;
                return -1;
            }

            internal BellyInfo(float waistWidth, float waistHeight, float sphereRadius, float originalSphereRadius, 
                               Vector3 bodyTopScale, float currentMultiplier, float waistThick, Vector3 nHeightScale,
                               float bellyToBreastDist, Vector3 characterScale, bool meshRootDidMove = false) 
            {
                WaistWidth = waistWidth;
                WaistHeight = waistHeight;
                SphereRadius = sphereRadius;
                OriginalSphereRadius = originalSphereRadius;
                BodyTopScale = bodyTopScale;
                CurrentMultiplier = currentMultiplier;
                WaistThick = waistThick;
                NHeightScale = nHeightScale;
                BellyToBreastDist = bellyToBreastDist;
                CharacterScale = characterScale;
                MeshRootDidMove = meshRootDidMove;
            }

            //Determine if we need to recalculate the sphere radius (hopefully to avoid change in hip bones causing belly size to sudenly change)
            internal bool NeedsSphereRecalc(PregnancyPlusData data, float newMultiplier) 
            {
                if (!IsInitialized) return true;
                if (CurrentMultiplier != newMultiplier) return true;

                return false;
            }


            //Determine if we need to recalculate the bone distances (typically when character scale changes)
            internal bool NeedsBoneDistanceRecalc(Vector3 bodyTopScale, Vector3 nHeightScale, Vector3 charScale) 
            {
                if (!IsInitialized) return true;
                if (BodyTopScale != bodyTopScale) return true;
                if (NHeightScale != nHeightScale) return true;
                if (CharacterScale != charScale) return true;

                return false;
            }


            public string Log()
            {
                return $@" WaistWidth {WaistWidth} WaistHeight {WaistHeight} WaistThick {WaistThick} BellyToBreastDist {BellyToBreastDist}
                           BodyTopScale {BodyTopScale} NHeightScale {NHeightScale} CharacterScale {CharacterScale} TotalCharScale {TotalCharScale}
                           SphereRadius {SphereRadius} OriginalSphereRadius {OriginalSphereRadius}
                           ";
            }

        }
        

        /// <summary>
        /// An overload for MeshInflate() that allows you to pass an initial inflationSize param
        /// For quickly setting the size, without worrying about the other config params
        /// </summary>
        /// <param name="inflationSize">Sets inflation size from 0 to 40, clamped</param>
        /// <param name="checkForNewMesh">Lets you force bypass the check for values changed to check for new meshes</param>
        /// <param name="pluginConfigSliderChanged">Will treat as if some slider values changed, which they did in global plugin config</param>
        public bool MeshInflate(float inflationSize, bool checkForNewMesh = false, bool pluginConfigSliderChanged = false)
        {                  
            //Allow an initial size to be passed in, and sets it to the config           
            infConfig.inflationSize = Mathf.Clamp(inflationSize, 0, 40);            

            return MeshInflate(checkForNewMesh, false, pluginConfigSliderChanged);
        }

        /// <summary>
        /// An overload for MeshInflate() that allows you to pass existing card data as the first param
        /// </summary>
        /// <param name="cardData">Some prexisting PregnancyPlusData that we want to activate</param>
        /// <param name="checkForNewMesh">Lets you force bypass the check for values changed to check for new meshes</param>
        /// <param name="pluginConfigSliderChanged">Will treat as if some slider values changed, which they did in global plugin config</param>
        public bool MeshInflate(PregnancyPlusData cardData, bool checkForNewMesh = false, bool forceRecalc = false, bool pluginConfigSliderChanged = false)
        {                  
            //Allow an initial size to be passed in, and sets it to the config           
            infConfig = cardData;          

            return MeshInflate(checkForNewMesh, forceRecalc, pluginConfigSliderChanged);
        }


        /// <summary>
        /// Limit where you can and cannot trigger inflation.  Always in Studio and Maker. Conditionally in Story mode
        /// </summary>
        public bool AllowedToInflate() 
        {
            var storyModeEnabled = PregnancyPlusPlugin.StoryMode != null ? PregnancyPlusPlugin.StoryMode.Value : false;
            return StudioAPI.InsideStudio || MakerAPI.InsideMaker || (storyModeEnabled && infConfig.GameplayEnabled);
        }


        /// <summary>
        /// Just a helper function to combine searching for verts in a mesh, and then applying the transforms
        /// </summary>
        internal bool ComputeMeshVerts(SkinnedMeshRenderer smr, bool isClothingMesh = false) 
        {
            //The list of bones to get verticies for
            #if KK            
                var boneFilters = new string[] { "cf_s_spine02", "cf_s_waist01", "cf_s_waist02" };//"cs_s_spine01" optionally for wider affected area
            #elif HS2 || AI
                var boneFilters = new string[] { "cf_J_Spine02_s", "cf_J_Kosi01_s", "cf_J_Kosi02_s" };
            #endif

            var hasVerticies = GetFilteredVerticieIndexes(smr, PregnancyPlusPlugin.MakeBalloon.Value ? null : boneFilters);        

            //If no belly verts found, or existing verts already exists, then we can skip this mesh
            if (!hasVerticies) return false; 

            if (PregnancyPlusPlugin.debugLog) PregnancyPlusPlugin.Logger.LogInfo($" ");
            if (PregnancyPlusPlugin.debugLog) PregnancyPlusPlugin.Logger.LogInfo($"  ComputeMeshVerts > {smr.name}"); 
            return GetInflatedVerticies(smr, bellyInfo.SphereRadius, bellyInfo.WaistWidth, isClothingMesh);
        }


        /// <summary>
        /// See if we already have this mesh's indexes stored, if the slider values haven't changed then we dont need to recompute, just apply existing cumputed verts
        /// </summary>
        internal bool NeedsComputeVerts(SkinnedMeshRenderer smr, bool sliderHaveChanged) 
        {
            var renderKey = GetMeshKey(smr);
            //Do a quick check to see if we need to fetch the bone indexes again.  ex: on second call we should allready have them
            //This saves a lot on compute apparently!            
            var isInitialized = bellyVerticieIndexes.TryGetValue(renderKey, out bool[] existingValues);
            if (isInitialized)
            {
                //If the vertex count has not changed then we can skip this
                if (existingValues.Length == smr.sharedMesh.vertexCount) return sliderHaveChanged;
            }

            //When no mesh found key, or incorrect vert count, the mesh changed so we need to recompute
            return true;
        }


        /// <summary>
        /// Tried to correct cloth flattening when inflation is at max, by offsetting each vert based on the distance it is from the sphere center to the max sphere radius
        /// </summary>
        /// <param name="meshRootTf">The transform used to convert a mesh vector from local space to worldspace and back</param>
        /// <param name="sphereCenterWs">The center position of the inflation sphere</param>
        /// <param name="sphereRadius">The desired sphere radius</param>
        /// <param name="waistWidth">The average width of the characters waist</param>
        /// <param name="origVertWS">The original verticie's worldspace position</param>
        /// <param name="meshName">Used to determine inner vs outer mesh layers from a known list of names</param>
        internal float GetClothesFixOffset(Transform meshRootTf, Vector3 sphereCenterWs, float sphereRadius, float waistWidth, Vector3 origVertWS, string meshName) 
        {  
            //The size of the area to spread the flattened offsets over like shrinking center dist -> inflated dist into a small area shifted outside the radius.  So hard to explin with words...
            float shrinkBy = bellyInfo.ScaledWaistWidth/40 + (bellyInfo.ScaledWaistWidth/40 * GetInflationClothOffset());

            var inflatedVerWS = (origVertWS - sphereCenterWs).normalized * sphereRadius + sphereCenterWs;//Get the line we want to do measurements on            
            //We dont care about empty space at sphere center, move outwards a bit before determining vector location on the line
            float awayFromCenter = (bellyInfo.ScaledWaistWidth/3);

            //The total radial distance after removing the distance we want to ignore
            var totatDist = (sphereRadius - awayFromCenter);
            var originToEndDist = FastDistance(meshRootTf.InverseTransformPoint(origVertWS), meshRootTf.InverseTransformPoint(inflatedVerWS));
            //Get the positon on a line that this vector exists between flattenExtensStartAt -> to sphereRadius. Then shrink it to scale
            var offset = totatDist * shrinkBy - (Math.Abs((totatDist - originToEndDist)) * shrinkBy);

            //This is the total additional distance we want to move this vert away from sphere center
            return offset + GetClothLayerOffset(meshName);
        }


        /// <summary>
        /// There are two cloth layers, inner and outer. I've assigned each cloth layer a static offset. layers: 1 = skin tight, 2 = above skin tight.  This way each layer will have less chance of cliping through to the next
        /// </summary>
        internal float GetClothLayerOffset(string meshName) {            
            #if KK      
                string[] innerLayers = {"o_bra_a", "o_bra_b", "o_shorts_a", "o_shorts_b", "o_panst_garter1", "o_panst_a", "o_panst_b"};
            #elif HS2 || AI                
                string[] innerLayers = {"o_bra_a", "o_bra_b", "o_shorts_a", "o_shorts_b", "o_panst_garter1", "o_panst_a", "o_panst_b"};
            #endif            

            //If inner layer then it doesnt need an additional offset
            if (innerLayers.Contains(meshName)) 
            {
                return 0;
            }

            //The mininum distance offset for each cloth layer, adjusted by user
            float additonalOffset = (bellyInfo.ScaledWaistWidth/60) + ((bellyInfo.ScaledWaistWidth/60) * GetInflationClothOffset());

            //If outer layer then add the offset
            return additonalOffset;
        } 


        /// <summary>
        /// Get the distance from the characters feet to the belly button collapsed into a straight Y line.null  (Ignores animation and scale, gives true measurement)
        /// </summary>
        internal float GetBellyButtonLocalHeight() 
        {            
            //Calculate the belly button height by getting each bone distance from foot to belly button (even during animation the height is correct!)
            #if KK
                var bbHeight = PregnancyPlusHelper.BoneChainStraigntenedDistance(ChaControl, bellyInfo.TotalCharScale, "cf_j_foot_L", "cf_j_waist01");
            #elif HS2 || AI            
                var bbHeight = PregnancyPlusHelper.BoneChainStraigntenedDistance(ChaControl, bellyInfo.TotalCharScale, "cf_J_Toes01_L", "cf_J_Kosi01");                       
            #endif                      
            
            return bbHeight;
        }


        /// <summary>
        /// Calculate the initial sphere radius by taking the smaller of the wasit width or waist to rib height. This is pre InflationMultiplier
        /// </summary>
        internal float GetSphereRadius(float wasitToRibDist, float wasitWidth, Vector3 charScale) {
            //The float numbers are just arbitrary numbers that ended up looking porportional
            return Math.Min(wasitToRibDist/1.25f, wasitWidth/1.3f) * charScale.y;
        }


        /// <summary>   
        /// Move the sphereCenter this much up or down to place it better visually
        /// </summary>
        internal Vector3 GetBellyButtonOffsetVector(Transform fromPosition, float currentHeight) 
        {
            //Makes slight vertical adjustments to put the sphere at the correct point                  
            return fromPosition.up * GetBellyButtonOffset(currentHeight);     
        }


        /// <summary>   
        /// The belly center offset, thats needed to line it up with the belly button
        /// </summary>
        internal float GetBellyButtonOffset(float currentHeight) 
        {
            //Makes slight vertical adjustments to put the sphere at the correct point                  
            return 0.155f * currentHeight;     
        }



        internal float FastDistance(Vector3 firstPosition, Vector3 secondPosition) 
        {
            //Calculates distance faster than vector3.distance.
            Vector3 heading;
            float distanceSquared;
    
            heading.x = firstPosition.x - secondPosition.x;
            heading.y = firstPosition.y - secondPosition.y;
            heading.z = firstPosition.z - secondPosition.z;
    
            distanceSquared = heading.x * heading.x + heading.y * heading.y + heading.z * heading.z;
            return Mathf.Sqrt(distanceSquared);
        }
       

        /// <summary>   
        /// Compares current to last slider values, if they havent changed it returns false
        /// </summary>        
        internal bool NeedsMeshUpdate(bool pluginConfigSliderChanged = false) 
        {
            if (pluginConfigSliderChanged) return true;
            return infConfig.Equals(infConfigHistory);
        }


        /// <summary>   
        /// Clear all inflations and remove the known mesh verts
        /// </summary>   
        public void CleanSlate() {
            ResetInflation();
            var keyList = new List<string>(originalVertices.Keys);
            RemoveRenderKeys(keyList);
        }
        

        internal void RemoveRenderKeys(List<string> keysToRemove) 
        {
            //Chear out any tracked verticie dictionaries by render key
            foreach(var key in keysToRemove) 
            {
                RemoveRenderKey(key);
            }
        }


        internal void RemoveRenderKey(string keyToRemove) 
        {
            if (originalVertices.ContainsKey(keyToRemove)) originalVertices.Remove(keyToRemove);
            if (inflatedVertices.ContainsKey(keyToRemove)) inflatedVertices.Remove(keyToRemove);
            if (currentVertices.ContainsKey(keyToRemove)) currentVertices.Remove(keyToRemove);
            if (bellyVerticieIndexes.ContainsKey(keyToRemove)) bellyVerticieIndexes.Remove(keyToRemove);        
        }

        
        /// <summary>   
        /// Creates a mesh dictionary key based on mesh name and vert count. (because mesh names can be the same, vertex count makes it almost always unique)
        /// </summary>    
        internal string GetMeshKey(SkinnedMeshRenderer smr) 
        {
            return PregnancyPlusHelper.KeyFromNameAndVerts(smr.name, smr.sharedMesh.vertexCount);
        }


        /// <summary>
        /// If the vert is within the calculated normals radius, then consider it as an altered vert that needs normal recalculation when applying inflation
        ///  Hopefully this will reduce breast shadows for smaller bellies
        /// </summary>
        public void CalculateNormalsBoundary(float vertDistance, float vertNormalCaluRadius, int i, string renderKey)
        {
            if (vertDistance < vertNormalCaluRadius)
            {
                alteredVerticieIndexes[renderKey][i] = true;
            }
            else 
            {
                alteredVerticieIndexes[renderKey][i] = false;
            }
        }


        /// <summary>
        /// This will update all verticies with a lerp from originalVertices to inflatedVertices depending on the inflationSize config
        /// Only modifies belly verticies, and if none are found, no action taken.
        /// </summary>
        /// <param name="mesh">Target mesh to update</param>
        /// <param name="renderKey">The Shared Mesh render name, used in dictionary keys to get the current verticie values</param>
        /// <returns>Will return True if any verticies are changed</returns>
        internal bool ApplyInflation(SkinnedMeshRenderer smr, string renderKey) 
        {
            var infSize = infConfig.inflationSize;
            //Only inflate if the value is above 0  
            if (infSize.Equals(null) || infSize == 0) return false;      

            //Create an instance of sharedMesh so we don't modify the mesh shared between characters
            var meshCopy = (Mesh)UnityEngine.Object.Instantiate(smr.sharedMesh);    
            smr.sharedMesh = meshCopy;

            var sharedMesh = smr.sharedMesh;

            if (!sharedMesh.isReadable) 
            {
                if (PregnancyPlusPlugin.debugLog)  PregnancyPlusPlugin.Logger.LogInfo(
                     $"ApplyInflation > smr '{renderKey}' is not readable, skipping");
                return false;
            } 

            //Check key exists in dict, remnove it if it does not
            var exists = originalVertices.TryGetValue(renderKey, out var val);
            if (!exists) 
            {
                if (PregnancyPlusPlugin.debugLog)  PregnancyPlusPlugin.Logger.LogInfo(
                     $"ApplyInflation > smr '{renderKey}' does not exists, skipping");
                RemoveRenderKey(renderKey);
                return false;
            }

            var origVert = originalVertices[renderKey];
            var currentVert = currentVertices[renderKey];
            var bellyVertIndex = bellyVerticieIndexes[renderKey];

            if (bellyVertIndex.Length == 0) return false;
            infConfigHistory.inflationSize = infSize;

            var currentVertLength = currentVert.Length;
            for (int i = 0; i < currentVertLength; i++)
            {
                //If not a belly index verticie then skip the morph
                if (!PregnancyPlusPlugin.debugAllVerts && !bellyVertIndex[i]) continue;

                //Set the lerp size of the belly based on the users slider value
                currentVert[i] = Vector3.Lerp(origVert[i], inflatedVertices[renderKey][i], (infSize/40));
            }

            if (currentVert.Length != sharedMesh.vertexCount) 
            {
                PregnancyPlusPlugin.Logger.LogInfo(
                            $"ApplyInflation > smr.sharedMesh '{renderKey}' has incorrect vert count {currentVert.Length}|{sharedMesh.vertexCount}");
                return false;
            }

            if (PregnancyPlusPlugin.debugLog)  PregnancyPlusPlugin.Logger.LogInfo($" mesh did ApplyInflation > {smr.name}");

            sharedMesh.vertices = currentVert;
            sharedMesh.RecalculateBounds();
            NormalSolver.RecalculateNormals(sharedMesh, 40f, alteredVerticieIndexes[renderKey]);
            //sharedMesh.RecalculateNormals();  //old way that leaves skin seams
            sharedMesh.RecalculateTangents();

            return true;
        }    


        /// <summary>   
        /// Will reset all meshes stored in the mesh dictionaries to default positons
        /// </summary>   
        internal void ResetInflation() 
        {   
            //Resets all mesh inflations
            var keyList = new List<string>(originalVertices.Keys);

            //For every active meshRenderer key we have created
            foreach(var renderKey in keyList) 
            {
                var smr = PregnancyPlusHelper.GetMeshRenderer(ChaControl, renderKey);
                //Normally triggered when user changes clothes, the old clothes render wont be found
                if (smr == null) continue;                

                //Create an instance of sharedMesh so we don't modify the mesh shared between characters, that was a fun issue
                Mesh meshCopy = (Mesh)UnityEngine.Object.Instantiate(smr.sharedMesh);
                smr.sharedMesh = meshCopy;

                var sharedMesh = smr.sharedMesh;
                var hasValue = originalVertices.TryGetValue(renderKey, out Vector3[] origVerts); 

                //On change clothes original verts become useless, so skip this
                if (!hasValue) return;   

                if (!sharedMesh.isReadable) {
                    if (PregnancyPlusPlugin.debugLog)  PregnancyPlusPlugin.Logger.LogInfo(
                         $"ResetInflation > smr '{renderKey}' is not readable, skipping");
                        continue;
                } 

                if (!sharedMesh || origVerts.Equals(null) || origVerts.Length == 0) continue;
                if (origVerts.Length != sharedMesh.vertexCount) 
                {
                    PregnancyPlusPlugin.Logger.LogInfo(
                        $"ResetInflation > smr '{renderKey}' has incorrect vert count {origVerts.Length}|{sharedMesh.vertexCount}");
                    continue;
                }

                sharedMesh.vertices = origVerts;
                sharedMesh.RecalculateBounds();
                NormalSolver.RecalculateNormals(sharedMesh, 40f, alteredVerticieIndexes[renderKey]);
                //sharedMesh.RecalculateNormals(); //old way that leaves skin seams
                sharedMesh.RecalculateTangents();
            }
        }

    }
}


