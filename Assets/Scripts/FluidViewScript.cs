using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FluidViewScript : MonoBehaviour 
{
    [SerializeField]
    private Mesh gridPointMesh;

    [SerializeField]
    private Material particleMat;

    struct MeshData
    {
        public Vector3 pos;
        public Vector3 normal;
    }

    private int meshVertCount;
    private const int gridResolution = 96;
    private int particlePointCount = gridResolution * gridResolution * gridResolution;
    private ComputeBuffer _meshBuffer;
    private int meshStride = 24;
    private ComputeBuffer _particleBuffer;
    private int gridStride = 12;
    
    void Start ()
    {
        meshVertCount = gridPointMesh.triangles.Length;
        _meshBuffer = GetMeshBuffer();
        _particleBuffer = GetParticleBuffer();
	}

    private ComputeBuffer GetMeshBuffer()
    {
        MeshData[] meshVerts = new MeshData[meshVertCount];
        ComputeBuffer ret = new ComputeBuffer(meshVertCount, meshStride);
        for (int i = 0; i < meshVertCount; i++)
        {
            meshVerts[i].pos = gridPointMesh.vertices[gridPointMesh.triangles[i]];
            meshVerts[i].normal = gridPointMesh.normals[gridPointMesh.triangles[i]];
        }
        ret.SetData(meshVerts);
        return ret;
    }
    
    private float GetNoiseFactor()
    {
        float random = UnityEngine.Random.value;
        random = (random - .5f) * 2;
        float ret = (float)1 / gridResolution;
        ret *= random;
        return ret;
    }

    private ComputeBuffer GetParticleBuffer()
    {
        List<Vector3> data = new List<Vector3>(particlePointCount);
        ComputeBuffer ret = new ComputeBuffer(particlePointCount, gridStride);
        for (int i = 0; i < gridResolution; i++)
        {
            for (int j = 0; j < gridResolution; j++)
            {
                for (int k = 0; k < gridResolution; k++)
                {
                    Vector3 datum = new Vector3
                    (
                        (float)i / gridResolution + GetNoiseFactor(),
                        (float)j / gridResolution + GetNoiseFactor(),
                        (float)k / gridResolution + GetNoiseFactor()
                    );
                    data.Add(datum);
                }
            }
        }
        ret.SetData(data.ToArray());
        return ret;
    }

    void OnRenderObject ()
    {
        particleMat.SetBuffer("_MeshBuffer", _meshBuffer);
        particleMat.SetBuffer("_ParticleBuffer", _particleBuffer);
        particleMat.SetPass(0);
        Graphics.DrawProcedural(MeshTopology.Quads, meshVertCount, particlePointCount);
	}
}
