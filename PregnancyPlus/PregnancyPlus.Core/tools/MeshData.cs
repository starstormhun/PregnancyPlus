using System.Collections.Generic;
using UnityEngine;

namespace KK_PregnancyPlus
{
    //Contains the mesh vert data for each character mesh.  Used to compute the belly shape
    public class MeshData
    {        
        public Vector3[] originalVertices;//The original untouched verts from the mesh
        public Vector3[] _inflatedVertices;//The verts from the mesh after being inflated       
        public Vector3[] smoothedVertices;//The inflated verts with lapacian smoothing applied (Use selected smooth belly mesh button)     
        public float[] clothingOffsets;//The distance we want to offset each vert from the body mesh when inflated
        public bool[] bellyVerticieIndexes;//When an index is True, that vertex is near the belly area
        public bool[] alteredVerticieIndexes;//When an index is True that vertex's position has been altered by GetInflatedVerticies()
        public float yOffset = 0;//The distance the mesh needs to be offset to match all other meshes y height, since some are not properly imported at the correct height

        //Need to clear out smoothed verts when inflated are ever set
        public Vector3[] inflatedVertices
        {
            get { return _inflatedVertices; }
            set 
            {
                smoothedVertices = null; 
                _inflatedVertices = value;
            }
        }

        
        public bool HasInflatedVerts
        {
            get {return _inflatedVertices != null && _inflatedVertices.Length > 0;}
        }

        public bool HasSmoothedVerts
        {
            get {return smoothedVertices != null && smoothedVertices.Length > 0;}
        }

        public bool HasOriginalVerts
        {
            get {return originalVertices != null && originalVertices.Length > 0;}
        }

        public bool HasClothingOffsets
        {
            get {return clothingOffsets != null && clothingOffsets.Length > 0;}
        }

        public int VertexCount
        {
            get {return _inflatedVertices == null ? 0 : _inflatedVertices.Length;}
        }


        //Initialize some fields, we will popupate others as needed
        public MeshData(int vertCount)
        {           
            bellyVerticieIndexes = new bool[vertCount];
            alteredVerticieIndexes = new bool[vertCount];
        }
    }
}