using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEngine.Profiling;

public class CustomDeferredRenderPipeline : RenderPipeline
{
    // DeferredCameraRenderer renderer = new DeferredCameraRenderer();
    // public CustomDefferedRenderPipeline () {
    // 	GraphicsSettings.useScriptableRenderPipelineBatching = true;
    // }

    RenderTexture gdepth;                                               // depth attachment
    RenderTexture[] gbuffers = new RenderTexture[4];                    // color attachments
    RenderTargetIdentifier gdepthID;
    RenderTargetIdentifier[] gbufferID = new RenderTargetIdentifier[4]; // tex ID 
    // RenderTexture lightPassTex;                                         // 存储 light pass 的结果
    // RenderTexture hizBuffer;                                            // hi-z buffer

    Matrix4x4 vpMatrix;
    Matrix4x4 vpMatrixInv;
    Matrix4x4 vpMatrixPrev;     // 上一帧的 vp 矩阵
    Matrix4x4 vpMatrixInvPrev;

    // IBL 贴图
    public Cubemap diffuseIBL;
    public Cubemap specularIBL;
    public Texture brdfLut;


    public CustomDeferredRenderPipeline()
    {
        QualitySettings.vSyncCount = 0;     // 关闭垂直同步
        Application.targetFrameRate = 60;   // 帧率
        // 创建纹理

        gdepth = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.Depth, RenderTextureReadWrite.Linear);
        gbuffers[0] = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        gbuffers[1] = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB2101010, RenderTextureReadWrite.Linear);
        gbuffers[2] = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB64, RenderTextureReadWrite.Linear);
        gbuffers[3] = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);

        gdepthID = gdepth;
        for (int i = 0; i < 4; i++)
            gbufferID[i] = gbuffers[i];

    }
    protected override void Render(
        ScriptableRenderContext context, Camera[] cameras
    )
    {
        //  gbuffer 
        Shader.SetGlobalTexture("_gdepth", gdepth);
        // Shader.SetGlobalTexture("_hizBuffer", hizBuffer);
        for (int i = 0; i < 4; i++)
            Shader.SetGlobalTexture("_GT" + i, gbuffers[i]);

        foreach (Camera camera in cameras)
        {
            // 设置相机矩阵
            Matrix4x4 viewMatrix = camera.worldToCameraMatrix;
            Matrix4x4 projMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
            vpMatrix = projMatrix * viewMatrix;
            vpMatrixInv = vpMatrix.inverse;
            Shader.SetGlobalMatrix("_vpMatrix", vpMatrix);
            Shader.SetGlobalMatrix("_vpMatrixInv", vpMatrixInv);
            Shader.SetGlobalMatrix("_vpMatrixPrev", vpMatrixPrev);
            Shader.SetGlobalMatrix("_vpMatrixInvPrev", vpMatrixInvPrev);

            // 设置 IBL 贴图
            Shader.SetGlobalTexture("_diffuseIBL", diffuseIBL);
            Shader.SetGlobalTexture("_specularIBL", specularIBL);
            Shader.SetGlobalTexture("_brdfLut", brdfLut);

            bool isEditor = Handles.ShouldRenderGizmos();

            // Here starts all pass//
            Cullpass(context, camera);
            GbufferPass(context, camera);
            LightPass(context, camera);
            // Here ends all pass//
            // skybox and Gizmos
            context.DrawSkybox(camera);
            if (isEditor)
            {
                context.DrawGizmos(camera, GizmoSubset.PreImageEffects);
                context.DrawGizmos(camera, GizmoSubset.PostImageEffects);
            }

            // 提交绘制命令
            context.Submit();
        }
    }
    bool Cullpass(ScriptableRenderContext context, Camera camera)
    {
        if (camera.TryGetCullingParameters(out ScriptableCullingParameters p))
        {
            var cullingResults = context.Cull(ref p);
            return true;
        }
        return false;
    }
    void GbufferPass(ScriptableRenderContext context, Camera camera)
    {
        Profiler.BeginSample("gbufferDraw");

        context.SetupCameraProperties(camera);
        CommandBuffer cmd = new CommandBuffer();
        cmd.name = "gbuffer";

        // 清屏
        cmd.SetRenderTarget(gbufferID, gdepth);
        cmd.ClearRenderTarget(true, true, Color.clear);
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();

        // 剔除
        camera.TryGetCullingParameters(out var cullingParameters);
        var cullingResults = context.Cull(ref cullingParameters);

        // config settings
        ShaderTagId shaderTagId = new ShaderTagId("gbuffer");   // 使用 LightMode 为 gbuffer 的 shader
        SortingSettings sortingSettings = new SortingSettings(camera);
        DrawingSettings drawingSettings = new DrawingSettings(shaderTagId, sortingSettings);
        FilteringSettings filteringSettings = FilteringSettings.defaultValue;

        // 绘制一般几何体
        context.DrawRenderers(cullingResults, ref drawingSettings, ref filteringSettings);
        context.Submit();

        Profiler.EndSample();
    }
    void LightPass(ScriptableRenderContext context, Camera camera)
    {// 光照 Pass : 计算 PBR 光照并且存储到 lightPassTex 纹理
        // 使用 Blit
        CommandBuffer cmd = new CommandBuffer();
        cmd.name = "lightpass";

        Material mat = new Material(Shader.Find("Custom RP/lightpass"));
        cmd.Blit(gbufferID[0], BuiltinRenderTextureType.CameraTarget, mat);
        context.ExecuteCommandBuffer(cmd);
    }
}