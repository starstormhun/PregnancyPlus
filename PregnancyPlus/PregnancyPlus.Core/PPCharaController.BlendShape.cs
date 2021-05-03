﻿using KKAPI;
using KKAPI.Chara;
using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using MessagePack;

#if HS2 || AI
    using AIChara;
#endif

namespace KK_PregnancyPlus
{

    //This partial class contains the blendshape logic for KK Timelines (and VNGE in future)
    public partial class PregnancyPlusCharaController: CharaCustomFunctionController
    {           

        //Keep track of which meshes are given blendshapes for the GUI to make the slider list
        internal List<SkinnedMeshRenderer> meshWithBlendShapes = new List<SkinnedMeshRenderer>();


        //Allows us to identify which mesh a blendshape belongs to when loading character cards
        [MessagePackObject(keyAsPropertyName: true)]
        public class MeshBlendShape
        {
            public string MeshName;//like smr.name
            public int VertCount;//To differentiate 2 meshes with the same names use vertex count comparison
            public BlendShapeController.BlendShape BlendShape;//Just a single Frame for now, though its possible to have multiple frames

            public MeshBlendShape(string meshName, BlendShapeController.BlendShape blendShape, int vertCount) 
            {
                MeshName = meshName;
                BlendShape = blendShape;
                VertCount = vertCount;
            }
        }


        /// <summary>
        /// On user button click. Create blendshape from current belly state.  Add it to infConfig so it will be saved to char card if the user chooses save scene
        /// </summary>
        /// <param name="temporary">If Temporary, the blendshape will not be saved to char card</param>
        /// <returns>boolean true if any blendshapes were created</returns>
        internal bool OnCreateBlendShapeSelected(bool temporary = false) 
        {
            if (PregnancyPlusPlugin.DebugLog.Value)  PregnancyPlusPlugin.Logger.LogInfo($" ");
            if (PregnancyPlusPlugin.DebugLog.Value)  PregnancyPlusPlugin.Logger.LogInfo($" OnCreateBlendShapeSelected ");

            var meshBlendShapes = new List<MeshBlendShape>();
            meshWithBlendShapes = new List<SkinnedMeshRenderer>();

            //Get all cloth renderes and attempt to create blendshapes from preset inflatedVerticies
            var clothRenderers = PregnancyPlusHelper.GetMeshRenderers(ChaControl.objClothes);
            meshBlendShapes = LoopAndCreateBlendShape(clothRenderers, meshBlendShapes, true);

            //do the same for body meshs
            var bodyRenderers = PregnancyPlusHelper.GetMeshRenderers(ChaControl.objBody);
            meshBlendShapes = LoopAndCreateBlendShape(bodyRenderers, meshBlendShapes);

            //Save any meshBlendShapes to card
            if (!temporary) AddBlendShapesToData(meshBlendShapes);

            //Reset belly size to 0 so the blendshape can be used with out interference
            PregnancyPlusGui.ResetSlider(PregnancyPlusGui.inflationSize, 0);

            //Append the smrs that have new blendspahes to the GUI to be seen
            blendShapeGui.OnSkinnedMeshRendererBlendShapesCreated(meshWithBlendShapes);

            return meshBlendShapes.Count > 0;
        }


        internal void OnOpenBlendShapeSelected()
        {
            //GUI blendshape popup, with existing blendshapes if any exists
            blendShapeGui.OpenBlendShapeGui(meshWithBlendShapes, this);
        }


        /// <summary>
        /// When the user wants to remove all existing PregnancyPlus blendshapes
        /// </summary>
        internal void OnRemoveAllBlendShapes()
        {
            meshWithBlendShapes = new List<SkinnedMeshRenderer>();
            ClearBlendShapesFromData();
        }


        /// <summary>
        /// Loop through each skinned mesh rendere and if it has inflated verts, create a blendshape from them
        /// </summary>
        /// <param name="smrs">List of skinnedMeshRenderes</param>
        /// <param name="meshBlendShapes">the current list of MeshBlendShapes collected so far</param>
        /// <returns>Returns final list of MeshBlendShapes we want to store in char card</returns>
        internal List<MeshBlendShape> LoopAndCreateBlendShape(List<SkinnedMeshRenderer> smrs, List<MeshBlendShape> meshBlendShapes, bool isClothingMesh = false) 
        {
            foreach(var smr in smrs) 
            {                
                var renderKey = GetMeshKey(smr);
                var exists = inflatedVertices.ContainsKey(renderKey);

                //Dont create blend shape if no inflated verts exists
                if (!exists || inflatedVertices[renderKey].Length < 0) continue;

                var blendShapeCtrl = CreateBlendShape(smr, renderKey);
                //Return the blendshape format that can be saved to character card
                var meshBlendShape = ConvertToMeshBlendShape(smr.name, blendShapeCtrl.blendShape);
                if (meshBlendShape != null) 
                {
                    meshBlendShapes.Add(meshBlendShape);                
                    meshWithBlendShapes.Add(smr);
                }

                // LogMeshBlendShapes(smr);
            }  

            return meshBlendShapes;
        }
     

        /// <summary>
        /// Convert a BlendShape to MeshBlendShape, used for storing to character card data
        /// </summary>
        internal MeshBlendShape ConvertToMeshBlendShape(string smrName, BlendShapeController.BlendShape blendShape) 
        {            
            if (blendShape == null) return null;
            var meshBlendShape = new MeshBlendShape(smrName, blendShape, blendShape.vertexCount);
            return meshBlendShape;
        }


        /// <summary>
        /// Sets a custom meshBlendShape object to character data
        /// </summary>
        /// <param name="meshBlendShapes">the list of MeshBlendShapes we want to save</param>
        internal void AddBlendShapesToData(List<MeshBlendShape> meshBlendShapes) 
        {            
            infConfig.meshBlendShape = MessagePack.LZ4MessagePackSerializer.Serialize(meshBlendShapes);
        }


        /// <summary>
        /// Clears any card data blendshapes
        /// </summary>
        internal void ClearBlendShapesFromData() 
        {            
            infConfig.meshBlendShape = null;
        }


        /// <summary>
        /// Loads a blendshape from character card and sets it to the correct mesh
        /// </summary>
        /// <param name="data">The characters card data for this plugin</param>
        internal void LoadBlendShapes(PregnancyPlusData data) 
        {
            if (data.meshBlendShape == null) return;
            if (PregnancyPlusPlugin.DebugLog.Value)  PregnancyPlusPlugin.Logger.LogInfo($" MeshBlendShape size > {data.meshBlendShape.Length/1024}KB ");

            meshWithBlendShapes = new List<SkinnedMeshRenderer>();

            //Unserialize the blendshape from characters card
            var meshBlendShapes = MessagePack.LZ4MessagePackSerializer.Deserialize<List<MeshBlendShape>>(data.meshBlendShape);
            if (meshBlendShapes == null || meshBlendShapes.Count <= 0) return;

            //For each stores meshBlendShape
            foreach(var meshBlendShape in meshBlendShapes)
            {
                //Loop through all meshes and find matching name
                var clothRenderers = PregnancyPlusHelper.GetMeshRenderers(ChaControl.objClothes, true);
                LoopMeshAndAddExistingBlendShape(clothRenderers, meshBlendShape, true);

                //do the same for body meshs
                var bodyRenderers = PregnancyPlusHelper.GetMeshRenderers(ChaControl.objBody, true);
                LoopMeshAndAddExistingBlendShape(bodyRenderers, meshBlendShape);
            }            
        }


        /// <summary>
        /// Loop through each mesh, and if the name/vertexcount matches, append the blendshape
        /// </summary>
        /// <param name="smrs">List of skinnedMeshRenderers to check for matching mesh name</param>
        /// <param name="meshBlendShape">The MeshBlendShape loaded from character card</param>
        internal void LoopMeshAndAddExistingBlendShape(List<SkinnedMeshRenderer> smrs, MeshBlendShape meshBlendShape, bool isClothingMesh = false) 
        {
            var meshName = meshBlendShape.MeshName;
            var vertexCount = meshBlendShape.VertCount;
            var blendShape = meshBlendShape.BlendShape; 
            
            foreach (var smr in smrs) 
            {              
                //If mesh matches, append the blend shape
                if (smr.name == meshName && smr.sharedMesh.vertexCount == vertexCount) 
                {
                    meshWithBlendShapes.Add(smr);

                    //Make sure the blendshape does not already exists
                    if (BlendShapeAlreadyExists(smr, meshBlendShape.BlendShape)) continue;

                    //Add the blendshape to the mesh
                    new BlendShapeController(smr.sharedMesh, blendShape, smr);

                    // LogMeshBlendShapes(smr);
                }                
            }              
        }
                

        /// <summary>
        /// Check whether the blendshape already exists
        /// </summary>
        internal bool BlendShapeAlreadyExists(SkinnedMeshRenderer smr, BlendShapeController.BlendShape blendShape) 
        {
            var shapeIndex = smr.sharedMesh.GetBlendShapeIndex(blendShape.name);
            //If the shape exists then true
            return (shapeIndex >= 0);
        }


        //just for debugging
        public void LogMeshBlendShapes(SkinnedMeshRenderer smr) 
        {
            var bsCount = smr.sharedMesh.blendShapeCount;

            //For each existing blend shape
            for (var i = 0; i < bsCount; i++)
            {
                Vector3[] deltaVertices = new Vector3 [smr.sharedMesh.vertexCount];
                Vector3[] deltaNormals = new Vector3 [smr.sharedMesh.vertexCount];
                Vector3[] deltaTangents = new Vector3 [smr.sharedMesh.tangents.Length];

                var name = smr.sharedMesh.GetBlendShapeName(i);
                var weight = smr.sharedMesh.GetBlendShapeFrameWeight(i, 0);
                var frameCount = smr.sharedMesh.GetBlendShapeFrameCount(i);
                smr.sharedMesh.GetBlendShapeFrameVertices(i, 0, deltaVertices, deltaNormals, deltaTangents);

                if (PregnancyPlusPlugin.DebugLog.Value) PregnancyPlusPlugin.Logger.LogInfo($" LogMeshBlendShapes > {name} shapeIndex {i} weight {weight} frameCount {frameCount} deltaVertices {deltaVertices.Length}");            
            }
        }


        
        /// <summary>
        /// This will create a blendshape frame for a mesh, that can be used in timeline, required there be a renderKey for inflatedVertices for this smr
        /// </summary>
        /// <param name="smr">Target mesh renderer to update (original shape)</param>
        /// <param name="renderKey">The Shared Mesh render name, used in dictionary keys to get the current verticie values</param>
        /// <param name="blendShapeTag">Optional blend shape tag to append to the blend shape name, used for identification if needed</param>
        /// <returns>Returns the MeshBlendShape that is created. Can be null</returns>
        internal BlendShapeController CreateBlendShape(SkinnedMeshRenderer smr, string renderKey, string blendShapeTag = null) 
        {     
            //Make a copy of the mesh. We dont want to affect the existing for this
            var meshCopyTarget = PregnancyPlusHelper.CopyMesh(smr.sharedMesh);   
            if (!meshCopyTarget.isReadable) 
            {
                PregnancyPlusPlugin.errorCodeCtrl.LogErrorCode(ChaControl.chaID, ErrorCode.PregPlus_MeshNotReadable, 
                    $"CreateBlendShape > smr '{renderKey}' is not readable, skipping");                     
                return null;
            } 

            //Make sure we have an existing belly shape to work with (can be null if user hasnt used sliders yet)
            var exists = originalVertices.TryGetValue(renderKey, out var val);
            if (!exists) 
            {
                if (PregnancyPlusPlugin.DebugLog.Value)  PregnancyPlusPlugin.Logger.LogInfo(
                     $"CreateBlendShape > smr '{renderKey}' does not exists, skipping");
                return null;
            }

            if (originalVertices[renderKey].Length != meshCopyTarget.vertexCount) 
            {
                PregnancyPlusPlugin.errorCodeCtrl.LogErrorCode(ChaControl.chaID, ErrorCode.PregPlus_IncorrectVertCount, 
                    $"CreateBlendShape > smr.sharedMesh '{renderKey}' has incorrect vert count {originalVertices[renderKey].Length}|{meshCopyTarget.vertexCount}");  
                return null;
            }

            //Calculate the original normals, but don't show them.  We just want it for the blendshape shape destination
            meshCopyTarget.vertices = inflatedVertices[renderKey];
            meshCopyTarget.RecalculateBounds();
            NormalSolver.RecalculateNormals(meshCopyTarget, 40f, bellyVerticieIndexes[renderKey]);
            meshCopyTarget.RecalculateTangents();

            // LogMeshBlendShapes(smr);

            var blendShapeName = MakeBlendShapeName(renderKey, blendShapeTag);

            //Create a blend shape object on the mesh, and return the controller object
            return new BlendShapeController(smr.sharedMesh, meshCopyTarget, blendShapeName, smr);            
        }  

        internal string MakeBlendShapeName(string renderKey, string blendShapeTag = null) {
            return blendShapeTag == null ? $"{renderKey}_{PregnancyPlusPlugin.GUID}" : $"{renderKey}_{PregnancyPlusPlugin.GUID}_{blendShapeTag}";
        }


        /// <summary>
        /// Find a blendshape by name on a smr, and change its weight.  If it does not exists, create it.
        /// </summary>
        /// <param name="smr">Target mesh renderer to update (original shape)</param>
        /// <param name="renderKey">The Shared Mesh render name, used in dictionary keys to get the current verticie values</param>
        /// <param name="didCompute">Whether the blendshape needs to be remade because the mesh shape was altered</param>
        /// <param name="blendShapeTag">Optional blend shape tag to append to the blend shape name, used for identification if needed</param>
        internal bool ApplyBlendShapeWeight(SkinnedMeshRenderer smr, string renderKey, bool onlyInflationSizeChanged, string blendShapeTag = null) {

            var blendShapeName = MakeBlendShapeName(renderKey, blendShapeTag);
            //Try to find an existing blendshape by name
            BlendShapeController bsc = new BlendShapeController(smr, blendShapeName);
            
            //If not found then create it
            if (bsc.blendShape == null || !onlyInflationSizeChanged) bsc = CreateBlendShape(smr, renderKey, blendShapeTag);

            if (bsc.blendShape == null) {
                if (PregnancyPlusPlugin.DebugLog.Value)  PregnancyPlusPlugin.Logger.LogWarning(
                     $"UpdateBlendShapeWeight > There was a problem creating the blendshape ${blendShapeName}");
                return false;
            }
            
            //Update the weight to be the same as inflationSize value   
            return bsc.ApplyBlendShapeWeight(smr, infConfig.inflationSize);
        }
        

        /// <summary>
        /// Allows resetting a blendshape weight back to 0
        /// </summary>
        /// <param name="smr">Target mesh renderer to update (original shape)</param>
        /// <param name="renderKey">The Shared Mesh render name, used to calculate the blendshape name</param>
        /// <param name="blendShapeTag">Optional blend shape tag to append to the blend shape name, used for identification if needed</param>
        internal bool ResetBlendShapeWeight(SkinnedMeshRenderer smr, string renderKey, string blendShapeTag = null) {
            var blendShapeName = MakeBlendShapeName(renderKey, blendShapeTag);

            //Try to find an existing blendshape by name
            BlendShapeController bsc = new BlendShapeController(smr, blendShapeName);
            if (bsc.blendShape == null) return false;

            return bsc.ApplyBlendShapeWeight(smr, 0);
        }
    }
}


