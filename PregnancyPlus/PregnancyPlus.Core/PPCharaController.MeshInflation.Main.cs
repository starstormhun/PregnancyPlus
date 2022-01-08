﻿using KKAPI;
using KKAPI.Chara;
using UnityEngine;
using System;
using System.Threading;
using System.Collections;
using System.Collections.Generic;
#if HS2 || AI
    using AIChara;
#endif

namespace KK_PregnancyPlus
{

    //This partial class contains the main mesh inflation logic
    public partial class PregnancyPlusCharaController: CharaCustomFunctionController
    {           

        /// <summary>
        /// Triggers belly mesh inflation for the current ChaControl for any active meshs (not hidden clothes)
        /// It will check the inflationSize dictionary for a valid value (last set via config slider or MeshInflate(value))
        /// If size 0 is used it will clear all active mesh inflations
        /// This will not run twice for the same parameters, a change of config value is required
        /// </summary>
        /// <param name="meshInflateFlags">Contains any flags needed for mesh computation</param>
        /// <returns>Will return True if the mesh was altered and False if not</returns>
        public void MeshInflate(MeshInflateFlags meshInflateFlags, string callee)
        {
            if (ChaControl.objBodyBone == null) return;//Make sure chatacter objs exists first  
            if (!PregnancyPlusPlugin.AllowMale.Value && ChaControl.sex == 0) return;// Only female characters, unless plugin config says otherwise          

            //Only continue if one of the config values changed, or we need to recompute a mesh
            if (!meshInflateFlags.NeedsToRun) return;

            if (!AllowedToInflate()) return;//if outside studio/maker, make sure StoryMode is enabled first
            if (!infConfig.GameplayEnabled) 
            {
                //Remove belly if gameplay disabled, and char has a belly
                if (infConfig.inflationSize > 0 && md?.Keys.Count > 0) 
                {
                    CleanSlate();
                }
                return;
            }

            //Resets all stored vert values, so the script will have to recalculate all from base body
            if (meshInflateFlags.freshStart) CleanSlate();

            //Only continue when size above 0
            if (infConfig.inflationSize <= 0 && !meshInflateFlags.bypassWhen0 && !isDuringInflationScene) 
            {
                infConfigHistory.inflationSize = 0;
                ResetInflation();
                return;                                
            }
            
            if (PregnancyPlusPlugin.DebugLog.Value || PregnancyPlusPlugin.DebugCalcs.Value)  PregnancyPlusPlugin.Logger.LogInfo($" ---------- {callee}() ");
            if (PregnancyPlusPlugin.DebugLog.Value || PregnancyPlusPlugin.DebugCalcs.Value)  PregnancyPlusPlugin.Logger.LogInfo($" inflationSize > {infConfig.inflationSize} for {charaFileName} ");            
            meshInflateFlags.Log();
            if (PregnancyPlusPlugin.DebugLog.Value || PregnancyPlusPlugin.DebugCalcs.Value)  PregnancyPlusPlugin.Logger.LogInfo($" ");

            //Get the measurements that determine the base belly size
            var hasMeasuerments = MeasureWaistAndSphere(ChaControl, meshInflateFlags.reMeasure);    
            //If the character is visible and can't get measurements, throw warning                 
            if (!hasMeasuerments && lastVisibleState) 
            {
                PregnancyPlusPlugin.errorCodeCtrl.LogErrorCode(charaFileName, ErrorCode.PregPlus_BadMeasurement, 
                    $"Could not get one or more belly measurements from character (This is normal when a character is loaded but inactive)");
                return;
            } else if (!hasMeasuerments && !lastVisibleState) 
            {
                if (PregnancyPlusPlugin.DebugLog.Value) PregnancyPlusPlugin.Logger.LogInfo($" Character not visible, can't take belly measurements yet {charaFileName}");  
                return; 
            }       

            //Get all mesh renderers, calculate, and apply inflation changes
            var bodyRenderers = PregnancyPlusHelper.GetMeshRenderers(ChaControl.objBody, findAll: true);
            LoopAndApplyMeshChanges(bodyRenderers, meshInflateFlags);
            var clothRenderers = PregnancyPlusHelper.GetMeshRenderers(ChaControl.objClothes);            
            LoopAndApplyMeshChanges(clothRenderers, meshInflateFlags, true, GetBodyMeshRenderer());       
            //TODO I guess we're including accessories now?                    
            var accessoryRenderers = PregnancyPlusHelper.GetMeshRenderers(ChaControl.objAccessory);            
            LoopAndApplyMeshChanges(accessoryRenderers, meshInflateFlags, true, GetBodyMeshRenderer());                           
        }


        /// <summary>
        /// Loop through each skinned mesh rendere and get its belly verts, then apply inflation when needed
        /// </summary>
        /// <param name="smrs">List of skinnedMeshRenderes</param>
        /// <param name="meshInflateFlags">Contains any flags needed for mesh computation</param>
        /// <param name="isClothingMesh">If this smr is a cloth mesh</param>
        /// <returns>boolean true if any meshes were changed</returns>
        internal void LoopAndApplyMeshChanges(List<SkinnedMeshRenderer> smrs, MeshInflateFlags meshInflateFlags, 
                                              bool isClothingMesh = false, SkinnedMeshRenderer bodyMeshRenderer = null) 
        {
            foreach (var smr in smrs) 
            {           
                var threadedCompute = false;//Whether the computation has been threaded
                var renderKey = GetMeshKey(smr);
                if (renderKey == null) continue;

                //Dont recompute verts if no sliders have changed or clothing added
                var needsComputeVerts = NeedsComputeVerts(smr, renderKey, meshInflateFlags);
                if (needsComputeVerts)
                {
                    threadedCompute = ComputeMeshVerts(smr, isClothingMesh, bodyMeshRenderer, meshInflateFlags, renderKey);                                                                                   
                }

                //When threaded, the belly will be set later so we can skip it here (only used when full re-computation is needed)
                if (threadedCompute) continue;           

                //We only make it to here when the shape was previously computed, but we need to alter the blendshape weight
                var appliedMeshChanges = ApplyInflation(smr, renderKey, meshInflateFlags.OverWriteMesh, blendShapeTempTagName, meshInflateFlags.bypassWhen0);

                //When inflation is actively happening as clothing changes, add cloting to the inflation list
                if (isDuringInflationScene) AppendToQuickInflateList(smr);
                if (appliedMeshChanges) infConfigHistory = (PregnancyPlusData)infConfig.Clone(); 
            }  

        }


        /// <summary>
        /// See if we already have this mesh's indexes stored, if the slider values haven't changed then we dont need to recompute, just apply existing cumputed verts
        /// </summary>
        public bool NeedsComputeVerts(SkinnedMeshRenderer smr, string renderKey, MeshInflateFlags meshInflateFlags) 
        {    
            //If mesh is on ignore list, skip it
            if (ignoreMeshList.Contains(renderKey)) return false;           
            if (meshInflateFlags.freshStart) return true;

            //Do a quick check to see if we need to fetch the bone indexes again.  ex: on second call we should allready have them
            //This saves a lot on compute apparently!            
            var isMeshInitialized = md.TryGetValue(renderKey, out MeshData _md);
            if (isMeshInitialized)
            {
                //If the vertex count has not changed then we can skip this if no critical sliders changed
                if (_md.bellyVerticieIndexes.Length == smr.sharedMesh.vertexCount) 
                {
                    if (meshInflateFlags.OnlyInflationSizeChanged) return false;
                    return meshInflateFlags.SliderHaveChanged;
                }
            }

            //When no mesh found key, or incorrect vert count, the mesh changed so we need to recompute
            return true;
        } 


        /// <summary>
        /// Just a helper function to combine searching for verts in a mesh, and then applying the transforms
        /// </summary>
        internal bool ComputeMeshVerts(SkinnedMeshRenderer smr, bool isClothingMesh, SkinnedMeshRenderer bodyMeshRenderer, MeshInflateFlags meshInflateFlags, string renderKey) 
        {
            //The list of bones to get verticies for (Belly area verts)
            #if KK            
                var boneFilters = new string[] { "cf_s_spine02", "cf_s_waist01", "cf_s_waist02" };//"cs_s_spine01" optionally for wider affected area
            #elif HS2 || AI
                var boneFilters = new string[] { "cf_J_Spine02_s", "cf_J_Kosi01_s", "cf_J_Kosi02_s" };
            #endif

            var hasVerticies = true;
            var isMeshInitialized = md.TryGetValue(renderKey, out MeshData _md);

            //Only fetch belly vert list when needed since its fairly expensive
            if (meshInflateFlags.NeedsToComputeIndex || !isMeshInitialized)
            {
                hasVerticies = GetFilteredVerticieIndexes(smr, PregnancyPlusPlugin.MakeBalloon.Value ? null : boneFilters);        
            }

            //If no belly verts found, or verts already cached, then we can skip this mesh
            if (!hasVerticies) return false; 

            if (PregnancyPlusPlugin.DebugLog.Value || PregnancyPlusPlugin.DebugCalcs.Value) PregnancyPlusPlugin.Logger.LogInfo($" ");
            if (PregnancyPlusPlugin.DebugLog.Value || PregnancyPlusPlugin.DebugCalcs.Value) PregnancyPlusPlugin.Logger.LogInfo($" Compute Mesh Verts for {smr.name}");

            //Get the newly created/or existing MeshData obj
            md.TryGetValue(renderKey, out _md);

            //On first pass we need to skin the mesh to a T-pose before computing the inflated verts (Threaded as well)
            if (_md.isFirstPass)
            {
                if (PregnancyPlusPlugin.DebugLog.Value || PregnancyPlusPlugin.DebugCalcs.Value) PregnancyPlusPlugin.Logger.LogInfo($" Computing BindPoses for {smr.name}");
                return ComputeBindPoseMesh(smr, bodyMeshRenderer, isClothingMesh, meshInflateFlags);            
            }

            return GetInflatedVerticies(smr, bellyInfo.SphereRadius, isClothingMesh, bodyMeshRenderer, meshInflateFlags);
        }


        /// <summary>
        /// Compute and cache the bind pose (T-pose) mesh positions, we need this to ignore all character animations when computing the belly shape, 
        ///     and to align the meshes correctly for cloths offset calculations.true  This is only computed once per mesh
        /// </summary>
        internal bool ComputeBindPoseMesh(SkinnedMeshRenderer smr, SkinnedMeshRenderer bodySmr, bool isClothingMesh, MeshInflateFlags meshInflateFlags)
        {
            if (smr == null) 
            {
                if (PregnancyPlusPlugin.DebugLog.Value) PregnancyPlusPlugin.Logger.LogWarning($" ComputeBindPoseMesh smr was null"); 
                return false;
            }

            //If mesh is not readable, make it so
            if (!smr.sharedMesh.isReadable) nativeDetour.Apply();

            var rendererName = GetMeshKey(smr);
            //initialize original verts if not already
            if (md[rendererName].originalVertices == null) md[rendererName].originalVertices = new Vector3[smr.sharedMesh.vertexCount];           

            //Fixes some meshes in KK having incorrectly offset y direction bind poses
            var bindPoseOffset = Matrix4x4.identity;
            #if KK
                bindPoseOffset = MeshSkinning.OffsetBindPosePosition(smr, bodySmr, ChaControl, isClothingMesh, UncensorCOMName);  
                md[rendererName].bindPoseCorrection = bindPoseOffset; 
            #endif            

            Matrix4x4[] boneMatrices = null;
            BoneWeight[] boneWeights = null;
            Vector3[] unskinnedVerts = null; 
        
            if (PregnancyPlusPlugin.DebugCalcs.Value) MeshSkinning.ShowBindPose(smr, bindPoseOffset);    

            //Matricies used to compute the T-pose mesh
            boneMatrices = MeshSkinning.GetBoneMatrices(smr, bindPoseOffset);
            boneWeights = smr.sharedMesh.boneWeights;
            unskinnedVerts = smr.sharedMesh.vertices;   

            //Thread safe lists and objects below            
            var origVerts = md[rendererName].originalVertices;
            var vertsLength = origVerts.Length;
            var smrTfTransPt = smr.transform.localToWorldMatrix;
            
            nativeDetour.Undo();

            //Heavy compute task below, run in separate thread
            WaitCallback threadAction = (System.Object stateInfo) => 
            {
                //Compute and store T-pose skinned mesh verts
                for (int i = 0; i < vertsLength; i++)
                {
                    //Get the skinned vert position from the T-pose bindpose we computed earlier
                    origVerts[i] = MeshSkinning.UnskinnedToSkinnedVertex(unskinnedVerts[i], smrTfTransPt, boneMatrices, boneWeights[i]);
                }

                //When this thread task is complete, execute the below in main thread
                Action threadActionResult = () => 
                {
                    md[rendererName].isFirstPass = false;

                    //Now that the mesh is skinned to T-pose, we can compute the inflation (If not `isFirstPass` ComputeBindPoseMesh() is skipped )
                    GetInflatedVerticies(smr, bellyInfo.SphereRadius, isClothingMesh, bodySmr, meshInflateFlags);
                };

                //Append to result queue.  Will execute on next Update()
                threading.AddResultToThreadQueue(threadActionResult);
            };
            
            
            //Start this threaded task, and will be watched in Update() for completion
            threading.Start(threadAction);            

            return true;
        }


        /// <summary>
        /// Does the vertex morph calculations to make a sphere out of the belly verticies, and updates the vertex dictionaries apprporiately
        /// </summary>
        /// <param name="skinnedMeshRenderer">The mesh renderer target</param>
        /// <param name="sphereRadius">The radius of the inflation sphere</param>
        /// <param name="isClothingMesh">Clothing requires a few tweaks to match skin morphs</param>
        /// <returns>Will return True if mesh verticies > 0 were found  Some meshes wont have any verticies for the belly area, returning false</returns>
        internal bool GetInflatedVerticies(SkinnedMeshRenderer smr, float sphereRadius, bool isClothingMesh, 
                                           SkinnedMeshRenderer bodySmr, MeshInflateFlags meshInflateFlags) 
        {
            if (smr == null) 
            {
                if (PregnancyPlusPlugin.DebugLog.Value) PregnancyPlusPlugin.Logger.LogWarning($" GetInflatedVerticies smr was null"); 
                return false;
            }

            //Found out body mesh can be nested under cloth game objects...   Make sure to flag it as non-clothing
            if (isClothingMesh && BodyNestedUnderCloth(smr, bodySmr)) 
            {
                PregnancyPlusPlugin.errorCodeCtrl.LogErrorCode(charaFileName, ErrorCode.PregPlus_BodyMeshDisguisedAsCloth, 
                    $" body mesh {smr.name} was nested under cloth object {smr.transform.parent.name}.  This is usually not an issue.");
                isClothingMesh = false;            
            }
            
            // if (PregnancyPlusPlugin.DebugLog.Value) PregnancyPlusPlugin.Logger.LogInfo($" SMR pos {smr.transform.position} rot {smr.transform.rotation} parent {smr.transform.parent}");                     
            if (!smr.sharedMesh.isReadable) nativeDetour.Apply();          

            var rendererName = GetMeshKey(smr);        
            //Dont erase originals since they should never change
            if (md[rendererName].originalVertices == null) md[rendererName].originalVertices = new Vector3[smr.sharedMesh.vertexCount];
            md[rendererName].inflatedVertices = new Vector3[smr.sharedMesh.vertexCount];
            md[rendererName].alteredVerticieIndexes = new bool[smr.sharedMesh.vertexCount];
            var bindPoseCorrection = md[rendererName].bindPoseCorrection;

            //set sphere center and allow for adjusting its position from the UI sliders  
            Vector3 sphereCenter = GetSphereCenter();            

            //Create mesh collider to make clothing measurements from skin (if it doesnt already exists)         
            if (NeedsClothMeasurement(smr, bodySmr, sphereCenter)) CreateMeshCollider(bodySmr); 
           
            //Get the cloth offset for each cloth vertex via raycast to skin
            //  Unfortunately this cant be inside the thread below because Unity Raycast are not thread safe...
            float[] clothOffsets = DoClothMeasurement(smr, bodySmr, sphereCenter);
            if (clothOffsets == null) clothOffsets = new float[md[rendererName].originalVertices.Length];

            var origVerts = md[rendererName].originalVertices;
            var inflatedVerts = md[rendererName].inflatedVertices;
            var bellyVertIndex = md[rendererName].bellyVerticieIndexes;
            var alteredVerts = md[rendererName].alteredVerticieIndexes;

            //Pre compute some values needed by SculptInflatedVerticie, doin it here saves on compute in the big loop
            var vertsLength = origVerts.Length;
            var preMorphSphereCenter = sphereCenter - GetUserMoveTransform();
            //calculate the furthest back morph point based on the back bone position, include character rotation
            var backExtentPos = new Vector3(preMorphSphereCenter.x, sphereCenter.y, preMorphSphereCenter.z) + Vector3.forward * -bellyInfo.ZLimit;
            //calculate the furthest top morph point based under the breast position, include character animated height differences
            var topExtentPos = new Vector3(preMorphSphereCenter.x, preMorphSphereCenter.y, preMorphSphereCenter.z) + Vector3.up * bellyInfo.YLimit;
            var vertNormalCaluRadius = sphereRadius + bellyInfo.WaistWidth/10;//Only recalculate normals for verts within this radius to prevent shadows under breast at small belly sizes          
            var waistWidth = bellyInfo.WaistWidth;

            //Lock in current slider values for threaded calculation
            var infConfigClone = (PregnancyPlusData)infConfig.Clone();

            //Animation curves are not thread safe, so make copies here
            var bellySidesAC = new ThreadsafeCurve(BellySidesAC);
            var bellyTopAC = new ThreadsafeCurve(BellyTopAC);
            var bellyEdgeAC = new ThreadsafeCurve(BellyEdgeAC);

            logCharMeshInfo(md[rendererName], smr, sphereCenter);

            nativeDetour.Undo();

            //Heavy compute task below, run in separate thread
            WaitCallback threadAction = (System.Object stateInfo) => 
            {
                var reduceClothFlattenOffset = 0f;

                #if DEBUG
                    var bellyVertsCount = 0;
                    for (int i = 0; i < bellyVertIndex.Length; i++)
                    {
                        if (bellyVertIndex[i]) bellyVertsCount++;
                    }
                    if (PregnancyPlusPlugin.DebugCalcs.Value) PregnancyPlusPlugin.Logger.LogInfo($" Mesh affected vert count {bellyVertsCount}");
                #endif               

                //For each vert, compute it's new inflated position if it is a belly vert
                for (int i = 0; i < vertsLength; i++)
                {
                    //Only care about altering belly verticies
                    if (!bellyVertIndex[i] && !PregnancyPlusPlugin.DebugVerts.Value) 
                    {
                        inflatedVerts[i] = origVerts[i];
                        continue;                
                    }

                    //Get the original skinned vertex position (T-pose) shifted to 0,0,0
                    var origVertLs = origVerts[i];                
                    var vertDistance = Vector3.Distance(origVertLs, sphereCenter);                    

                    //Ignore verts outside the sphere radius
                    if (vertDistance > vertNormalCaluRadius && !PregnancyPlusPlugin.DebugVerts.Value) 
                    {
                        inflatedVerts[i] = origVerts[i];
                        continue;                
                    }
                    
                    Vector3 inflatedVertLs;                    
                    Vector3 verticieToSpherePos;       
                    reduceClothFlattenOffset = 0f; 

                    // If the vert is within the calculated normals radius, then consider it as an altered vert that needs normal recalculation when applying inflation
                    // This also means we can ignore other verts later saving compute time
                    //  Hopefully this will reduce breast shadows for smaller bellies
                    if (vertDistance <= vertNormalCaluRadius) alteredVerts[i] = true;                                                                          
                    
                    if (isClothingMesh) 
                    {                        
                        //Calculate clothing offset distance                   
                        reduceClothFlattenOffset = GetClothesFixOffset(infConfigClone, sphereCenter, sphereRadius, waistWidth, origVertLs, smr.name, clothOffsets[i]);
                    }
                        
                    //Shift each belly vertex away from sphere center in a sphere pattern.  This is the core of the Preg+ belly shape
                    verticieToSpherePos = (origVertLs - sphereCenter).normalized * (sphereRadius + reduceClothFlattenOffset) + sphereCenter;                                                    

                    //Make adjustments to the shape to make it smooth, and feed in user slider input
                    inflatedVertLs =  SculptInflatedVerticie(infConfigClone, origVertLs, verticieToSpherePos, sphereCenter, waistWidth, 
                                                             preMorphSphereCenter, sphereRadius, backExtentPos, topExtentPos, 
                                                             bellySidesAC, bellyTopAC, bellyEdgeAC);   

                    //store the new inflated vert, unshifted from 0,0,0                                                           
                    inflatedVerts[i] = inflatedVertLs;                                                  
                }                  

                //When this thread task is complete, execute the below in main thread
                Action threadActionResult = () => 
                {
                    //If you need to debug the calculated vert positions visually
                    if (PregnancyPlusPlugin.DebugLog.Value) 
                    {

                        //Debug mesh with spheres, and include mesh offset
                        // DebugTools.DebugMeshVerts(smr, origVerts, new Vector3(0, md[rendererName].yOffset, 0));

                        //Some other internally measured points/boundaries
                        // if (PregnancyPlusPlugin.DebugLog.Value) DebugTools.DrawSphereAndAttach(smr.transform, 0.2f, sphereCenter);
                        // if (PregnancyPlusPlugin.DebugLog.Value) DebugTools.DrawLine(topExtentPos, topExtentPos + Vector3.back * 4);                        
                        // if (PregnancyPlusPlugin.DebugLog.Value) DebugTools.DrawLine(topExtentPos + Vector3.down * topExtentPos.y/10, topExtentPos + Vector3.down * topExtentPos.y/10 + Vector3.back * 4);                        
                        // if (PregnancyPlusPlugin.DebugLog.Value) DebugTools.DrawLine(backExtentPos, backExtentPos + Vector3.left * 4);                        
                        // if (PregnancyPlusPlugin.DebugLog.Value) DebugTools.DrawLine(sphereCenter, sphereCenter + Vector3.forward * 1);  
                        // if (PregnancyPlusPlugin.DebugLog.Value) DebugTools.DrawSphere(0.1f, preMorphSphereCenter);
                        // if (PregnancyPlusPlugin.DebugLog.Value) DebugTools.DrawLine(Vector3.zero, Vector3.zero + Vector3.forward * 1);  
                       
                        // if (PregnancyPlusPlugin.DebugLog.Value && isClothingMesh) DebugTools.DrawLineAndAttach(smr.transform, 1, smr.sharedMesh.bounds.center - yOffsetDir);
                    }                                                   

                    //Apply computed mesh back to body
                    var appliedMeshChanges = ApplyInflation(smr, rendererName, meshInflateFlags.OverWriteMesh, blendShapeTempTagName, meshInflateFlags.bypassWhen0);

                    //When inflation is actively happening as clothing changes, make sure the new clothing grows too
                    if (isDuringInflationScene) AppendToQuickInflateList(smr);
                    if (appliedMeshChanges) infConfigHistory = infConfigClone;   
                };

                //Append to result queue.  Will execute on next Update()
                threading.AddResultToThreadQueue(threadActionResult);

            };

            //Start this threaded task, and will be watched in Update() for completion
            threading.Start(threadAction);

            return true;                 
        }


        /// <summary>
        /// Calculates the center position of the inflation sphere.  Including the user slider value
        /// </summary>
        internal Vector3 GetSphereCenter() 
        {             
            //Measure from feet to belly 
            //TODO now that everything is in localspace with BindPose, we can probaby just use the bone position as the height
            var bbHeight = GetBellyButtonLocalHeight();
            bellyInfo.BellyButtonHeight = bbHeight;           
            Vector3 bellyButtonPos = Vector3.up * bbHeight; 

            //Include user slider values "Move Y" and "Move Z"
            return bellyButtonPos + GetUserMoveTransform() + GetBellyButtonOffsetVector(bbHeight);                                         
        }


        /// <summary>
        /// This will take the sphere-ified verticies and apply smoothing to them via Lerps to remove sharp edges.  Make the belly more round
        /// </summary>
        /// <param name="originalVertice">The original verticie position</param>
        /// <param name="inflatedVerticie">The target verticie position, after sphere-ifying</param>
        /// <param name="sphereCenterPos">The center of the imaginary sphere</param>
        /// <param name="waistWidth">The characters waist width that limits the width of the belly (future implementation)</param>
        /// <param name="preMorphSphereCenter">The original sphere center location, before user slider input</param>
        /// <param name="backExtentPos">The point behind which no mesh changes allowed</param>
        /// <param name="topExtentPos">The point above which no mesh changes allowed</param>
        internal Vector3 SculptInflatedVerticie(PregnancyPlusData infConfigClone, Vector3 originalVerticeLs, Vector3 inflatedVerticieLs, Vector3 sphereCenterLs, float waistWidth, 
                                                Vector3 preMorphSphereCenter, float sphereRadius, Vector3 backExtentPos, Vector3 topExtentPos, 
                                                ThreadsafeCurve bellySidesAC, ThreadsafeCurve bellyTopAC, ThreadsafeCurve bellyEdgeAC) 
        {
            //No smoothing modification in debug mode
            if (PregnancyPlusPlugin.MakeBalloon.Value || PregnancyPlusPlugin.DebugVerts.Value) return inflatedVerticieLs;                       
            
            //get the smoothing distance limits so we don't have weird polygons and shapes on the edges, and prevents morphs from shrinking past original skin boundary
            var pmSkinToCenterDist = Math.Abs(Vector3.Distance(preMorphSphereCenter, originalVerticeLs));
            var pmInflatedToCenterDist = Math.Abs(Vector3.Distance(preMorphSphereCenter, inflatedVerticieLs));
            var skinToCenterDist = Math.Abs(Vector3.Distance(sphereCenterLs, originalVerticeLs));
            var inflatedToCenterDist = Math.Abs(Vector3.Distance(sphereCenterLs, inflatedVerticieLs));
            
            // PregnancyPlusPlugin.Logger.LogInfo($" preMorphSphereCenter {preMorphSphereCenter} sphereCenterWs {sphereCenterWs} meshRootTf.pos {meshRootTf.position}");

            //Only apply morphs if the imaginary sphere is outside of the skins boundary (Don't want to shrink anything inwards, only out)
            if (skinToCenterDist >= inflatedToCenterDist || pmSkinToCenterDist > pmInflatedToCenterDist) return originalVerticeLs; 
            
            //Get the base shape with XY plane size limits
            var smoothedVectorLs = SculptBaseShape(originalVerticeLs, inflatedVerticieLs, sphereCenterLs);                 

            //Allow user adjustment of the height and width placement of the belly
            if (GetInflationShiftY(infConfigClone) != 0 || GetInflationShiftZ(infConfigClone) != 0) 
            {
                smoothedVectorLs = GetUserShiftTransform(infConfigClone, smoothedVectorLs, sphereCenterLs, skinToCenterDist);            
            }

            //Allow user adjustment of the width of the belly
            if (GetInflationStretchX(infConfigClone) != 0) 
            {   
                smoothedVectorLs = GetUserStretchXTransform(infConfigClone, smoothedVectorLs, sphereCenterLs);
            }

            //Allow user adjustment of the height of the belly
            if (GetInflationStretchY(infConfigClone) != 0) 
            {   
                smoothedVectorLs = GetUserStretchYTransform(infConfigClone, smoothedVectorLs, sphereCenterLs);
            }

            if (GetInflationRoundness(infConfigClone) != 0) 
            {  
                smoothedVectorLs = GetUserRoundnessTransform(infConfigClone, originalVerticeLs, smoothedVectorLs, sphereCenterLs, skinToCenterDist, bellyEdgeAC);
            }

            //Allow user adjustment of the egg like shape of the belly
            if (GetInflationTaperY(infConfigClone) != 0) 
            {
                smoothedVectorLs = GetUserTaperYTransform(infConfigClone, smoothedVectorLs, sphereCenterLs, skinToCenterDist);
            }

            //Allow user adjustment of the front angle of the belly
            if (GetInflationTaperZ(infConfigClone) != 0) 
            {
                smoothedVectorLs = GetUserTaperZTransform(infConfigClone, originalVerticeLs, smoothedVectorLs, sphereCenterLs, skinToCenterDist, backExtentPos);
            }

            //Allow user adjustment of the fat fold line through the middle of the belly
            if (GetInflationFatFold(infConfigClone) > 0) 
            {
                smoothedVectorLs = GetUserFatFoldTransform(infConfigClone, originalVerticeLs, smoothedVectorLs, sphereCenterLs, sphereRadius);
            }            

            //If the user has selected a drop slider value
            if (GetInflationDrop(infConfigClone) > 0) 
            {
                smoothedVectorLs = GetUserDropTransform(infConfigClone, Vector3.up, smoothedVectorLs, sphereCenterLs, skinToCenterDist, sphereRadius);
            }

            //After all user transforms are applied, remove the edges from the sides/top of the belly
            smoothedVectorLs = RoundToSides(originalVerticeLs, smoothedVectorLs, backExtentPos, bellySidesAC);            

            //Less skin stretching under breast area with large slider values
            if (originalVerticeLs.y > preMorphSphereCenter.y)
            {                
                smoothedVectorLs = ReduceRibStretchingZ(originalVerticeLs, smoothedVectorLs, topExtentPos, bellyTopAC);
            }

            // //Experimental, move more polygons to the front of the belly at max, Measured by trying to keep belly button size the same at 0 and max inflation size
            // var bellyTipZ = (center.z + maxSphereRadius);
            // if (smoothedVector.z >= center.z)
            // {
            //     var invertLerpScale = smoothedVector.z/bellyTipZ - 0.75f;
            //     var bellyTipVector = new Vector3(0, center.y, bellyTipZ);
            //     //lerp towards belly point
            //     smoothedVector = Vector3.Slerp(smoothedVector, bellyTipVector, invertLerpScale);
            // }


            //At this point if the smoothed vector is still the originalVector just return it
            if (smoothedVectorLs.Equals(originalVerticeLs)) return smoothedVectorLs;


            //**** All of the below are post vert calculation checks to make sure the vertex position don't go outside of bounds (or inside the character)
  
            //Get core point on the same y plane as the original vert
            var coreLineVertLs = Vector3.up * originalVerticeLs.y;
            //Get core line from feet to head that verts must respect distance from
            var origCoreDist = Math.Abs(Vector3.Distance(originalVerticeLs, coreLineVertLs));
            //Do the same for the smoothed vert
            var coreLineSmoothedVertLs = Vector3.up * smoothedVectorLs.y;       
            var currentCoreDist = Math.Abs(Vector3.Distance(smoothedVectorLs, coreLineSmoothedVertLs)); 

            //Compute the new distances from vert to sphereCenters
            var currentVectorDistance = Math.Abs(Vector3.Distance(sphereCenterLs, smoothedVectorLs));
            var pmCurrentVectorDistance = Math.Abs(Vector3.Distance(preMorphSphereCenter, smoothedVectorLs)); 

            //** Order matters below **


            //Don't allow any morphs to shrink towards the characters core line any more than the original distance
            if (currentCoreDist < origCoreDist) 
            {
                //Since this is just an XZ distance check, don't modify the new y value
                smoothedVectorLs = new Vector3(originalVerticeLs.x, smoothedVectorLs.y, originalVerticeLs.z);
            }

            //Don't allow any morphs to shrink towards the sphere center more than its original distance, only outward morphs allowed
            if (skinToCenterDist > currentVectorDistance || pmSkinToCenterDist > pmCurrentVectorDistance) 
            {
                return originalVerticeLs;
            }

            //Don't allow any morphs to move behind the character's.z = 0 + extentOffset position, otherwise skin sometimes pokes out the back side :/
            if (backExtentPos.z > smoothedVectorLs.z) 
            {
                return originalVerticeLs;
            }

            //Don't allow any morphs to move behind the original verticie z position, only forward expansion (ignoring ones already behind sphere center)
            if (originalVerticeLs.z > smoothedVectorLs.z && originalVerticeLs.z > sphereCenterLs.z) 
            {
                //Get the average(not really average after all...) x and y change to move the new position halfway back to the oiriginal vert (hopefullt less strange triangles near belly to body edge)
                var yChangeAvg = (smoothedVectorLs.y - originalVerticeLs.y)/3;
                var xChangeAvg = (smoothedVectorLs.x - originalVerticeLs.x)/3;
                smoothedVectorLs = new Vector3(smoothedVectorLs.x - xChangeAvg, smoothedVectorLs.y - yChangeAvg, originalVerticeLs.z);
            }

            return smoothedVectorLs;             
        }
                
    }
}


