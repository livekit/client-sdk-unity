using UnityEngine;

/// <summary>
/// Builds a UV resize/crop matrix and creates a resize material for a resize mapping between source and target textures.
/// </summary>
[ExecuteAlways]
public class ResizeTextureController
{
    private Material _resizeMaterial;
    private Texture _sourceTexture;

    private RenderTexture _targetTexture;

    private float _targetWidth;
    private float _targetHeight;
    
    private const string SHADER_NAME = "Hidden/LiveKit/Resize";

    private CropMode _cropMode;  // How to fit src → target

    private int _rotationDegrees = -1;
    private bool _mirror;

    public enum CropMode
    {
        FillCrop,   // Scale to fill, crop the excess (e.g. 16:9 → 1:1 crops sides)
        Letterbox,  // Scale to fit,  add black bars
    }

    public ResizeTextureController(Texture sourceTexture, float targetWidth, float targetHeight, CropMode cropMode = CropMode.Letterbox, int rotationDegrees = 0, bool mirror = false)
    {
        var shader = Shader.Find(SHADER_NAME);
        _resizeMaterial = new Material(shader);
        _sourceTexture = sourceTexture;
        _targetWidth = targetWidth;
        _targetHeight = targetHeight;
        _cropMode = cropMode;

        _targetTexture = new RenderTexture((int)_targetWidth, (int)_targetHeight, 0, RenderTextureFormat.ARGB32);

        SetRotation(rotationDegrees, mirror);
    }

    /// <summary>
    /// Update the display rotation and mirror state. Cheap when nothing changed.
    /// </summary>
    public void SetRotation(int rotationDegrees, bool mirror)
    {
        int normalized = ((rotationDegrees % 360) + 360) % 360;
        if (normalized == _rotationDegrees && mirror == _mirror)
            return;

        _rotationDegrees = normalized;
        _mirror = mirror;

        bool swap = normalized == 90 || normalized == 270;
        float effSrcW = swap ? _sourceTexture.height : _sourceTexture.width;
        float effSrcH = swap ? _sourceTexture.width : _sourceTexture.height;

        float srcAspect = effSrcW / effSrcH;
        float targetAspect = (float)_targetTexture.width / _targetTexture.height;
        Matrix4x4 crop = BuildCropMatrix(srcAspect, targetAspect, _cropMode);
        Matrix4x4 rot = BuildRotationMirrorMatrix(normalized, mirror);
        _resizeMaterial.SetMatrix("_ResizeMatrix", rot * crop);
    }

    public RenderTexture GetTargetTexture()
    {
        return _targetTexture;
    }

    /// <summary>
    /// Resize the source texture into the target texture based on the controllers set crop mode.
    /// </summary>
    public void Resize()
    {        
        Graphics.Blit(_sourceTexture, _targetTexture, _resizeMaterial);
    }

    public void Dispose()
    {
        if (_targetTexture != null)
        {
            _targetTexture.Release();
            Object.Destroy(_targetTexture);
        }
    
        if (_resizeMaterial != null) 
            Object.Destroy(_resizeMaterial);
    }

    /// <summary>
    /// Returns a UV-space 4×4 matrix that maps [0,1]² destination UVs
    /// into the correct sub-region of the source texture.
    ///
    /// Convention: multiply as  uvTransformed = M * float4(uv, 0, 1)
    /// </summary>
    public static Matrix4x4 BuildCropMatrix(float srcAspect, float dstAspect,
                                             CropMode mode = CropMode.FillCrop)
    {
        float scaleX, scaleY, offsetX, offsetY;

        // Ratio of source aspect to destination aspect
        float ratio =  srcAspect / dstAspect;  // >1 → source is wider than target

        if (mode == CropMode.FillCrop)
        {
            if (ratio > 1f)
            {
                // Source is wider → crop left/right
                scaleX  = 1f / ratio;   // < 1 → narrows the UV window horizontally
                scaleY  = 1f;
                offsetX = (1f - scaleX) * 0.5f;
                offsetY = 0f;
            }
            else
            {
                // Source is taller → crop top/bottom
                scaleX  = 1f;
                scaleY  = ratio;        // < 1 → narrows the UV window vertically
                offsetX = 0f;
                offsetY = (1f - scaleY) * 0.5f;
            }
        }
        else // Letterbox
        {
            if (ratio > 1f)
            {
                // Source is wider → pillarbox
                scaleX  = 1f;
                scaleY  = ratio;
                offsetX = 0f;
                offsetY = (1f - scaleY) * 0.5f;
            }
            else
            {
                // Source is taller → letterbox
                scaleX  = 1f / ratio;
                scaleY  = 1f;
                offsetX = (1f - scaleX) * 0.5f;
                offsetY = 0f;
            }
        }

        // Build a 2D affine transform embedded in a 4×4 matrix:
        //  | scaleX   0      0  offsetX |
        //  |   0    scaleY   0  offsetY |
        //  |   0      0      1     0    |
        //  |   0      0      0     1    |
        var m = Matrix4x4.identity;
        m.m00 = scaleX;
        m.m11 = scaleY;
        m.m03 = offsetX;
        m.m13 = offsetY;
        return m;
    }

    /// <summary>
    /// UV-space matrix that maps post-rotation UV back to actual source UV,
    /// achieving a CW display rotation of `degrees` (0/90/180/270). When
    /// `flipSourceV` is true, additionally flips V in source space (used to
    /// undo WebCamTexture.videoVerticallyMirrored).
    /// </summary>
    public static Matrix4x4 BuildRotationMirrorMatrix(int degrees, bool flipSourceV)
    {
        // u_src = a*u + b*v + c
        // v_src = e*u + f*v + d
        float a, b, c, d, e, f;
        switch (((degrees % 360) + 360) % 360)
        {
            case 90:  a = 0;  b = -1; c = 1; e = 1;  f = 0;  d = 0; break;
            case 180: a = -1; b = 0;  c = 1; e = 0;  f = -1; d = 1; break;
            case 270: a = 0;  b = 1;  c = 0; e = -1; f = 0;  d = 1; break;
            default:  a = 1;  b = 0;  c = 0; e = 0;  f = 1;  d = 0; break;
        }
        if (flipSourceV)
        {
            // Apply V flip in source space: v_src -> 1 - v_src
            d = 1 - d;
            e = -e;
            f = -f;
        }
        var m = Matrix4x4.identity;
        m.m00 = a; m.m01 = b; m.m03 = c;
        m.m10 = e; m.m11 = f; m.m13 = d;
        return m;
    }
}