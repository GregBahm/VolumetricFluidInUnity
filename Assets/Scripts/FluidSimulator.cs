using System;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Rendering;

public class FluidSimulator : MonoBehaviour
{
    public float ImpulseRadius = 10;
    public float DyeDissipation = 0.95f;
    public float VelocityDissipation = 0.9999f;
    public float TimeStep = 0.01f;

    public Color InkColor;

    private static readonly Vector3 NumThreads = new Vector3(16, 16, 1);
    private static readonly Vector3 FluidSimResolution = new Vector3(64, 64, 64);

    public enum TextureType
    {
        Dye,
        Velocity,
        Divergence,
        Pressure
    }

    private struct BoundaryData
    {
        public Vector3 pixelCoords;
        public Vector3 insideOffset;
    }

    private const int BoundaryDataStride = (4 * 3) * 2;

    [SerializeField]
    private TextureType displayTexture = TextureType.Dye;

    [SerializeField]
    private Material displayCubeMat = null;

    [SerializeField]
    private ComputeShader fluidSimulationShader = null;

    private RenderTexture dyeTexture;
    private RenderTexture readDyeTexture;
    private RenderTexture velocityTexture;
    public RenderTexture VelocityTexture { get{ return velocityTexture; } }
    private RenderTexture readVelocityTexture;
    private RenderTexture divergenceTexture;
    private RenderTexture pressureTexture;
    private RenderTexture readPressureTexture;

    private ComputeBuffer boundaryBuffer;

    private int addImpulseKernelIndex;
    private int advectionKernelIndex;
    private int clearTexturesKernelIndex;
    private int computeDivergenceKernelIndex;
    private int jacobiKernelIndex;
    private int subtractGradientKernelIndex;
    private int drawPressureBoundaryKernelIndex;
    private int drawVelocityBoundaryKernelIndex;

    private int velocityTextureId;
    private int readVelocityId;
    private int dyeTextureId;
    private int readDyeId;
    private int resolutionId;
    private int clearTextureId;
    private int divergenceTextureId;
    private int readDivergenceId;
    private int pressureTextureId;
    private int readPressureId;
    private int boundaryValueId;
    private int boundaryDataBufferId;

    private int impulsePositionId;
    private int impulseDirectionId;

    private const int JacobiIterations = 50;

    private void Start()
    {
        addImpulseKernelIndex = fluidSimulationShader.FindKernel("AddImpulse");
        advectionKernelIndex = fluidSimulationShader.FindKernel("Advect");
        clearTexturesKernelIndex = fluidSimulationShader.FindKernel("ClearTextures");
        computeDivergenceKernelIndex = fluidSimulationShader.FindKernel("ComputeDivergence");
        jacobiKernelIndex = fluidSimulationShader.FindKernel("Jacobi");
        subtractGradientKernelIndex = fluidSimulationShader.FindKernel("SubtractGradient");
        drawPressureBoundaryKernelIndex = fluidSimulationShader.FindKernel("DrawPressureBoundary");
        drawVelocityBoundaryKernelIndex = fluidSimulationShader.FindKernel("DrawVelocityBoundary");

        velocityTextureId = Shader.PropertyToID("velocity");
        readVelocityId = Shader.PropertyToID("ReadVelocity");
        dyeTextureId = Shader.PropertyToID("dye");
        readDyeId = Shader.PropertyToID("ReadDye");
        resolutionId = Shader.PropertyToID("resolution");
        impulsePositionId = Shader.PropertyToID("impulsePosition");
        impulseDirectionId = Shader.PropertyToID("impulseDirection");
        clearTextureId = Shader.PropertyToID("clearTexture");
        divergenceTextureId = Shader.PropertyToID("divergence");
        readDivergenceId = Shader.PropertyToID("ReadDivergence");
        pressureTextureId = Shader.PropertyToID("pressure");
        readPressureId = Shader.PropertyToID("ReadPressure");
        boundaryValueId = Shader.PropertyToID("boundaryValue");
        boundaryDataBufferId = Shader.PropertyToID("boundaryData");

        dyeTexture = CreateTexture();
        readDyeTexture = CreateTexture();
        velocityTexture = CreateTexture();
        readVelocityTexture = CreateTexture();
        divergenceTexture = CreateTexture();
        pressureTexture = CreateTexture();
        readPressureTexture = CreateTexture();
        
        // Fluid sim properties
        fluidSimulationShader.SetVector(resolutionId, FluidSimResolution);

        InitializeBoundaryBuffer();

        AddImpulse(new Vector3(0.25f, 0.5f, 0.25f), new Vector3(1f, 1f, 1f));
        AddImpulse(new Vector3(0.25f, 0.5f, 0.25f), new Vector3(1f, 1f, 1f));

        AddImpulse(new Vector3(0.75f, 0.2f, 0.25f), new Vector3(-1f, 0f, 0f));
    }

    private void Update()
    {
        // ADVECTION
        Advect();
        
        // VELOCITY BOUNDARIES
        DrawVelocityBoundary();

        // VISCOSITY

        // COMPUTE DIVERGENCE
        ComputeDivergence();

        // TODO: BOUNDARIES

        ClearTexture(pressureTexture);
        ClearTexture(readPressureTexture);

        // JACOBI SOLVER
        for (int i = 0; i < JacobiIterations; i++)
        {
            RunJacobi();
            DrawPressureBoundary();
        }
        
        // SUBTRACT GRADIENT
        SubtractGradient();
        
        if (Input.GetKeyDown(KeyCode.X))
        {
            Debug.Log("Advect!");
            Advect();
        }

        if (Input.GetKeyDown(KeyCode.C))
        {
            Debug.Log("Clearing textures");

            ClearTexture(dyeTexture);
            ClearTexture(readDyeTexture);
        }
        displayCubeMat.SetTexture("_MainTex", GetTexture(displayTexture));
        fluidSimulationShader.SetFloat("impulseRadius", ImpulseRadius);
        fluidSimulationShader.SetFloat("dyeDissipation", DyeDissipation);
        fluidSimulationShader.SetFloat("velocityDissipation", VelocityDissipation);
        fluidSimulationShader.SetFloat("timestep", TimeStep);
        fluidSimulationShader.SetVector("inkColor", InkColor);
        Shader.SetGlobalTexture("_dyeTexture", dyeTexture);
        Shader.SetGlobalTexture("_velocityTexture", velocityTexture);
    }

    private Texture GetTexture(TextureType displayTexture)
    {
        switch (displayTexture)
        {
            case TextureType.Dye:
                return readDyeTexture;
            case TextureType.Velocity:
                return readVelocityTexture;
            case TextureType.Divergence:
                return divergenceTexture;
            case TextureType.Pressure:
            default:
                return pressureTexture;
        }

    }

    private void OnDestroy()
    {
        boundaryBuffer.Dispose();
    }


    private void InitializeBoundaryBuffer()
    {
#if !SIM3D
        int boundarySize = (int) (FluidSimResolution.x * FluidSimResolution.y * 2 +
                                  FluidSimResolution.x * FluidSimResolution.z * 2 +
                                  FluidSimResolution.y * FluidSimResolution.z * 2);

        boundaryBuffer = new ComputeBuffer(boundarySize, BoundaryDataStride);

        BoundaryData[] boundaryData = new BoundaryData[boundarySize];

        int currentIndex = 0;
        for (int i = 0; i < FluidSimResolution.x; i++)
        {
            for (int j = 0; j < FluidSimResolution.y; j++)
            {
                boundaryData[currentIndex].pixelCoords = new Vector3(i, j, 0);
                boundaryData[currentIndex].insideOffset = new Vector3(0, 0, 1);

                currentIndex++;

                boundaryData[currentIndex].pixelCoords = new Vector3(i, j, FluidSimResolution.z - 1);
                boundaryData[currentIndex].insideOffset = new Vector3(0, 0, -1);

                currentIndex++;
            }

            for (int k = 0; k < FluidSimResolution.z; k++)
            {
                boundaryData[currentIndex].pixelCoords = new Vector3(i, 0, k);
                boundaryData[currentIndex].insideOffset = new Vector3(0, 1, 0);

                currentIndex++;

                boundaryData[currentIndex].pixelCoords = new Vector3(i, FluidSimResolution.y - 1, k);
                boundaryData[currentIndex].insideOffset = new Vector3(0, -1, 0);

                currentIndex++;
            }
        }

        for (int j = 0; j < FluidSimResolution.y; j++)
        {
            for (int k = 0; k < FluidSimResolution.z; k++)
            {
                boundaryData[currentIndex].pixelCoords = new Vector3(0, j, k);
                boundaryData[currentIndex].insideOffset = new Vector3(1, 0, 0);

                currentIndex++;

                boundaryData[currentIndex].pixelCoords = new Vector3(FluidSimResolution.x - 1, j, k);
                boundaryData[currentIndex].insideOffset = new Vector3(-1, 0, 0);

                currentIndex++;
            }
        }
#else
        int boundarySize = (int)(FluidSimResolution.x * 2 + FluidSimResolution.y * 2);
        boundaryBuffer = new ComputeBuffer(boundarySize, BoundaryDataStride);

        BoundaryData[] boundaryData = new BoundaryData[boundarySize];

        int currentIndex = 0;
        for (int i = 0; i < FluidSimResolution.x; i++)
        {
            boundaryData[currentIndex].pixelCoords = new Vector3(i, 0, 0);
            boundaryData[currentIndex].insideOffset = new Vector3(0, 1, 0);

            currentIndex++;

            boundaryData[currentIndex].pixelCoords = new Vector3(i, FluidSimResolution.y - 1, 0);
            boundaryData[currentIndex].insideOffset = new Vector3(0, -1, 0);

            currentIndex++;
        }

        for (int j = 0; j < FluidSimResolution.y; j++)
        {
            boundaryData[currentIndex].pixelCoords = new Vector3(0, j, 0);
            boundaryData[currentIndex].insideOffset = new Vector3(1, 0, 0);

            currentIndex++;

            boundaryData[currentIndex].pixelCoords = new Vector3(FluidSimResolution.x - 1, j, 0);
            boundaryData[currentIndex].insideOffset = new Vector3(-1, 0, 0);

            currentIndex++;
        }
#endif

        Assert.IsTrue(currentIndex == boundarySize, "Make sure the boundary buffer was generated properly.");

        boundaryBuffer.SetData(boundaryData);
    }

    private void ClearTexture(RenderTexture texture)
    {
        fluidSimulationShader.SetTexture(clearTexturesKernelIndex, clearTextureId, texture);
        DispatchKernel(clearTexturesKernelIndex);
    }

    public void AddImpulse(Vector3 impulsePosition, Vector3 impulseDirection)
    {
        SetDyeTextures(addImpulseKernelIndex);
        SetVelocityTextures(addImpulseKernelIndex);

        fluidSimulationShader.SetFloats(impulsePositionId, impulsePosition.x, impulsePosition.y, impulsePosition.z);
        fluidSimulationShader.SetFloats(impulseDirectionId, impulseDirection.x, impulseDirection.y, impulseDirection.z);
        
        DispatchKernel(addImpulseKernelIndex);

        SwapDyeTextures();
        SwapVelocityTextures();
    }

    private void Advect()
    {
        SetDyeTextures(advectionKernelIndex);
        SetVelocityTextures(advectionKernelIndex);

        DispatchKernel(advectionKernelIndex);

        SwapDyeTextures();
        SwapVelocityTextures();
    }

    private void DrawVelocityBoundary()
    {
        SwapVelocityTextures();
        SetVelocityTextures(drawVelocityBoundaryKernelIndex);

        fluidSimulationShader.SetFloat(boundaryValueId, 0f);
        fluidSimulationShader.SetBuffer(drawVelocityBoundaryKernelIndex, boundaryDataBufferId, boundaryBuffer);

        fluidSimulationShader.Dispatch(drawVelocityBoundaryKernelIndex, Mathf.CeilToInt(boundaryBuffer.count / NumThreads.x), 1, 1);

        SwapVelocityTextures();
    }

    private void ComputeDivergence()
    {
        SetVelocityTextures(computeDivergenceKernelIndex);

        fluidSimulationShader.SetTexture(computeDivergenceKernelIndex, divergenceTextureId, divergenceTexture);

        DispatchKernel(computeDivergenceKernelIndex);
    }

    private void RunJacobi()
    {
        SetPressureTextures(jacobiKernelIndex);

        fluidSimulationShader.SetTexture(jacobiKernelIndex, readDivergenceId, divergenceTexture);

        DispatchKernel(jacobiKernelIndex);
    }

    private void DrawPressureBoundary()
    {
        // TODO: this kernel reads from the previous iteration of the pressure texture.
        // Need a kernel that copies the value as-is if it's not a boundary pixel and
        // copies the "inside pixel" value if it's a boundary pixel.

        SetPressureTextures(drawPressureBoundaryKernelIndex);

        fluidSimulationShader.SetFloat(boundaryValueId, 1f);
        fluidSimulationShader.SetBuffer(drawPressureBoundaryKernelIndex, boundaryDataBufferId, boundaryBuffer);

        fluidSimulationShader.Dispatch(drawPressureBoundaryKernelIndex, Mathf.CeilToInt(boundaryBuffer.count / NumThreads.x), 1, 1);

        SwapPressureTextures();
    }

    private void SubtractGradient()
    {
        SetPressureTextures(subtractGradientKernelIndex);
        SetVelocityTextures(subtractGradientKernelIndex);

        DispatchKernel(subtractGradientKernelIndex);

        SwapVelocityTextures();
    }

    private void DispatchKernel(int kernelIndex)
    {
        fluidSimulationShader.Dispatch(kernelIndex, Mathf.CeilToInt(FluidSimResolution.x / NumThreads.x), Mathf.CeilToInt(FluidSimResolution.y / NumThreads.y), Mathf.CeilToInt(FluidSimResolution.z / NumThreads.z));
    }

    private void SwapDyeTextures()
    {
        var temp = readDyeTexture;
        readDyeTexture = dyeTexture;
        dyeTexture = temp;
    }

    private void SetDyeTextures(int kernelIndex)
    {
        fluidSimulationShader.SetTexture(kernelIndex, dyeTextureId, dyeTexture);
        fluidSimulationShader.SetTexture(kernelIndex, readDyeId, readDyeTexture);
    }

    private void SwapVelocityTextures()
    {
        var temp = readVelocityTexture;
        readVelocityTexture = velocityTexture;
        velocityTexture = temp;
    }

    private void SetVelocityTextures(int kernelIndex)
    {
        fluidSimulationShader.SetTexture(kernelIndex, velocityTextureId, velocityTexture);
        fluidSimulationShader.SetTexture(kernelIndex, readVelocityId, readVelocityTexture);
    }
    
    private void SwapPressureTextures()
    {
        var temp = readPressureTexture;
        readPressureTexture = pressureTexture;
        pressureTexture = temp;
    }

    private void SetPressureTextures(int kernelIndex)
    {
        fluidSimulationShader.SetTexture(kernelIndex, pressureTextureId, pressureTexture);
        fluidSimulationShader.SetTexture(kernelIndex, readPressureId, readPressureTexture);
    }

    private static RenderTexture CreateTexture()
    {
        RenderTexture renderTexture = new RenderTexture((int)FluidSimResolution.x, (int)FluidSimResolution.y, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        renderTexture.dimension = TextureDimension.Tex3D;
        renderTexture.volumeDepth = (int)FluidSimResolution.z;
        renderTexture.enableRandomWrite = true;
        renderTexture.useMipMap = false;
        renderTexture.filterMode = FilterMode.Bilinear;
        renderTexture.wrapMode = TextureWrapMode.Clamp;
        renderTexture.Create();
        
        return renderTexture;
    }
}
