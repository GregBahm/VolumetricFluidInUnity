using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(FluidSimulator))]
public class FlowingParticlesScript : MonoBehaviour
{

    [SerializeField]
    private Material particleMat;

    [SerializeField]
    private ComputeShader particleCompute;

    [SerializeField]
    private float particleSize = 1;

    private int computeKernel;
    private const int batchSize = 128;

    private const int gridResolution = 128;
    private int particlePointCount = gridResolution * gridResolution * gridResolution;
    private ComputeBuffer _meshBuffer;
    private ComputeBuffer _particleBuffer;
    private ComputeBuffer _sourcePositions;
    private int gridStride = 12;

    private RenderTexture velocityTexture;

    void Start()
    {
        computeKernel = particleCompute.FindKernel("MoveFluidParticles");
        _meshBuffer = GetMeshBuffer();

        Vector3[] postionData = GetPositionData();
        _particleBuffer = new ComputeBuffer(particlePointCount, gridStride);
        _particleBuffer.SetData(postionData);
        _sourcePositions = new ComputeBuffer(particlePointCount, gridStride);
        _sourcePositions.SetData(postionData);
        
        velocityTexture = GetComponent<FluidSimulator>().VelocityTexture;
    }

    private ComputeBuffer GetMeshBuffer()
    {
        ComputeBuffer ret = new ComputeBuffer(6, 12);
        ret.SetData(new[]
        {
            new Vector3(-.5f, .5f),
            new Vector3(.5f, .5f),
            new Vector3(.5f, -.5f),
            new Vector3(.5f, -.5f),
            new Vector3(-.5f, -.5f),
            new Vector3(-.5f, .5f)
        });
        return ret;
    }

    private float GetNoiseFactor()
    {
        //return 0;
        float random = UnityEngine.Random.value;
        random = (random - .5f) * 2;
        float ret = (float)1 / gridResolution;
        ret *= random;
        return ret;
    }

    private Vector3[] GetPositionData()
    {
        List<Vector3> data = new List<Vector3>(particlePointCount);
        for (int i = 0; i < gridResolution; i++)
        {
            for (int j = 0; j < gridResolution; j++)
            {
                for (int k = 0; k < gridResolution; k++)
                {
                    Vector3 datum = new Vector3
                    (
                        (float)i / gridResolution + GetNoiseFactor(),
                        (float)j / gridResolution + GetNoiseFactor() / 2,
                        (float)k / gridResolution + GetNoiseFactor()
                    );
                    data.Add(datum);
                }
            }
        }
        return data.ToArray();
    }

    private void Update()
    {
        int batchCount = Mathf.CeilToInt((float)particlePointCount / batchSize);
        particleCompute.SetBuffer(computeKernel, "_ParticleBuffer", _particleBuffer);
        particleCompute.SetBuffer(computeKernel, "_SourcePositions", _sourcePositions);
        particleCompute.SetTexture(computeKernel, "VelocityField", velocityTexture);
        particleCompute.Dispatch(computeKernel, batchCount, 1, 1);

        particleMat.SetMatrix("_Transform", transform.localToWorldMatrix);
    }

    void OnRenderObject()
    {
        particleMat.SetBuffer("_MeshBuffer", _meshBuffer);
        particleMat.SetBuffer("_ParticleBuffer", _particleBuffer);
        particleMat.SetFloat("_ParticleSize", particleSize);
        particleMat.SetPass(0);
        Graphics.DrawProceduralNow(MeshTopology.Triangles, 6, particlePointCount);
    }
    private void OnDestroy()
    {
        _meshBuffer.Dispose();
        _particleBuffer.Dispose();
        _sourcePositions.Dispose();
    }
}
