using System;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;



using System.IO;
using System.Linq;

/** Enum used for Face effects - tiger or glasses model
 */
public enum FaceEffect
{
    Nothing=0,
    Normal = 1,
    Caricature=2,
    RealTexture=3
}

/** Class that implements the behavior of tracking application.
 * 
 * This is the core class that shows how to use visage|SDK capabilities in Unity. It connects with visage|SDK through calls to 
 * native methods that are implemented in VisageTrackerUnityPlugin.
 * It uses tracking data to transform objects that are attached to it in ControllableObjects list.
 */
public class Tracker : MonoBehaviour
{
    #region Properties

    


    [Header("Tracker configuration settings")]
    //Tracker configuration file name.
    public string ConfigFileEditor;
    public string ConfigFileStandalone;
    public string ConfigFileIOS;
    public string ConfigFileAndroid;
    public string ConfigFileOSX;
    public string ConfigFileWebGL;


    [Header("Tracking settings")]

    public const int MAX_FACES = 2;


    private bool trackerInited = false;

    [Header("Controllable object info")]

    public Transform[] ControllableObjectsTiger;
    Vector3[] startingPositionsTiger;
    Vector3[] startingRotationsTiger;

    // Mesh information
    private const int MaxVertices = 1024;
    private const int MaxTriangles = 2048;

    private int VertexNumber = 0;
    private int TriangleNumber = 0;
    private Vector2[] TexCoords = { };
    private Vector3[][] Vertices = new Vector3[MAX_FACES][];
    private int[] Triangles = { };
    private float[] vertices = new float[MaxVertices * 3];
    private int[] triangles = new int[MaxTriangles * 3];
    private float[] texCoords = new float[MaxVertices * 2];
    private MeshFilter meshFilter;
    private Vector2[] modelTexCoords;

    

    [Header("Tiger texture mapping file")]
    public TextAsset TexCoordinatesFile;

    [Header("Tracker output data info")]
    public Vector3[] Translation = new Vector3[MAX_FACES];
    public Vector3[] Rotation = new Vector3[MAX_FACES];
    private bool isTracking = false;
    public int[] TrackerStatus = new int[MAX_FACES];
    private float[] translation = new float[3];
    private float[] rotation = new float[3];

    [Header("Camera settings")]
    public Material CameraViewMaterial;
    public Shader CameraViewShaderRGBA;
    public Shader CameraViewShaderBGRA;
    public Shader CameraViewShaderUnlit;
    public float CameraFocus;
    public int Orientation = 0;
    private int currentOrientation = 0;
    public int isMirrored = 1;
    private int currentMirrored = 1;
    public int camDeviceId = 0;
    private int currentCamDeviceId = 0;
    public int defaultCameraWidth = -1;
    public int defaultCameraHeight = -1;
    private bool doSetupMainCamera = true;
    private bool camInited = false;

    [Header("Texture settings")]
    public int ImageWidth = 800;
    public int ImageHeight = 600;
    public int TexWidth = 512;
    public int TexHeight = 512;

    private TextureFormat TexFormat = TextureFormat.RGBA32;

    private Texture2D texture = null;
    private Color32[] texturePixels;
    private GCHandle texturePixelsHandle;

    [Header("GUI button settings")]
    public Button trackingButton;
    private bool stopTrackButton = false;
    public Button portrait;
    public Button portraitUpside;
    public Button landscapeRight;
    public Button landscapeLeft;
    private Sprite trackPlay;
    private Sprite trackPause;
    private FaceEffect currentEffect = FaceEffect.Nothing;


    [HideInInspector]
    public bool frameForAnalysis = false;
    public bool frameForRecog = false;
    private bool texCoordsStaticLoaded = false;



    #endregion

    #region Native code printing

    private bool enableNativePrinting = true;

    //For printing from native code
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void MyDelegate(string str);

    //Function that will be called from the native wrapper
    static void CallBackFunction(string str)
    {
        Debug.Log("::CallBack : " + str);
    }

    #endregion

    private void Awake()
    {



        // Set callback for printing from native code
        if (enableNativePrinting)
        {
            /*  MyDelegate callback_delegate = new MyDelegate(CallBackFunction);
                // Convert callback_delegate into a function pointer that can be
                // used in unmanaged code.
                IntPtr intptr_delegate = Marshal.GetFunctionPointerForDelegate(callback_delegate);
                // Call the API passing along the function pointer.
                VisageTrackerNative.SetDebugFunction(intptr_delegate);*/
        }




        string licenseFilePath = Application.streamingAssetsPath + "/" + "/Visage Tracker/";

        // Set license path depending on platform
        switch (Application.platform)
        {


            case RuntimePlatform.WebGLPlayer:
                licenseFilePath = "";
                break;
            case RuntimePlatform.WindowsEditor:
                licenseFilePath = Application.streamingAssetsPath + "/Visage Tracker/";
                break;
        }


        //NOTE: licensing for Windows platform expects folder path exclusively
        VisageTrackerNative._initializeLicense(licenseFilePath);


    }


    void Start()
    {

       

        // Create an empty mesh and load tiger texture coordinates
        meshFilter = GetComponent<MeshFilter>();
        meshFilter.mesh = new Mesh();

        // Set configuration file path and name depending on a platform
        string configFilePath = Application.streamingAssetsPath + "/" + ConfigFileStandalone;

        switch (Application.platform)
        {

            case RuntimePlatform.WebGLPlayer:
                configFilePath = ConfigFileWebGL;
                break;
            case RuntimePlatform.WindowsEditor:
                configFilePath = Application.streamingAssetsPath + "/" + ConfigFileEditor;
                break;
        }

        // Initialize tracker with configuration and MAX_FACES
        trackerInited = InitializeTracker(configFilePath);

        // Get current device orientation
        Orientation = GetDeviceOrientation();

        // Open camera in native code
        camInited = OpenCamera(Orientation, camDeviceId, defaultCameraWidth, defaultCameraHeight, isMirrored);

        

        // Initialize various containers for scene 3D objects (glasses, tiger texture)
        InitializeContainers();

        // Load sprites for play button
        LoadButtonSprites();

        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.OpenGLCore)
            Debug.Log("Notice: if graphics API is set to OpenGLCore, the texture might not get properly updated.");



    }


    void Update()
    {
        //signals analysis and recognition to stop if camera or tracker are not initialized and until new frame and tracking data are obtained
        frameForAnalysis = false;
        frameForRecog = false;

        if (!isTrackerReady())
            return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Application.Quit();
        }



        if (isTracking)
        {

            Orientation = GetDeviceOrientation();

            // Check if orientation or camera device changed
            if (currentOrientation != Orientation || currentCamDeviceId != camDeviceId || currentMirrored != isMirrored)
            {
                currentCamDeviceId = camDeviceId;
                currentOrientation = Orientation;
                currentMirrored = isMirrored;

                // Reopen camera with new parameters 
                OpenCamera(currentOrientation, currentCamDeviceId, defaultCameraWidth, defaultCameraHeight, currentMirrored);
                texture = null;
                doSetupMainCamera = true;

            }

            // grab current frame and start face tracking
            VisageTrackerNative._grabFrame();

            VisageTrackerNative._track();
            VisageTrackerNative._getTrackerStatus(TrackerStatus);

            //After the track has been preformed on the new frame, the flags for the analysis and recognition are set to true
            frameForAnalysis = true;
            frameForRecog = true;

            // Set main camera field of view based on camera information
            if (doSetupMainCamera)
            {
                // Get camera information from native
                VisageTrackerNative._getCameraInfo(out CameraFocus, out ImageWidth, out ImageHeight);
                float aspect = ImageWidth / (float)ImageHeight;
                float yRange = (ImageWidth > ImageHeight) ? 1.0f : 1.0f / aspect;
                Camera.main.fieldOfView = Mathf.Rad2Deg * 2.0f * Mathf.Atan(yRange / CameraFocus);
                doSetupMainCamera = false;
            }
        }

        RefreshImage();

        for (int i = 0; i < TrackerStatus.Length; ++i)
        {
            if (TrackerStatus[i] == (int)TrackStatus.OK)
            {


                if (!texCoordsStaticLoaded)
                {
                    texCoordsStaticLoaded = GetTextureCoordinates(out modelTexCoords);
                }
                
                UpdateControllableObjects(i);
            }
            else
                ResetControllableObjects(i);
        }
    }

    bool isTrackerReady()
    {
        if (camInited && trackerInited && !stopTrackButton)
        {
            isTracking = true;
            trackingButton.image.overrideSprite = trackPause;
        }
        else
        {
            isTracking = false;
        }
        return isTracking;
    }

    void OnDestroy()
    {

        camInited = !(VisageTrackerNative._closeCamera());

    }



    #region GUI Buttons OnClick events
    public void onButtonEffect()
    {
        if (currentEffect == FaceEffect.Nothing)
            currentEffect = FaceEffect.Normal;
        else if (currentEffect == FaceEffect.Normal)
            currentEffect = FaceEffect.Caricature;
        else if (currentEffect == FaceEffect.Caricature)
            currentEffect = FaceEffect.RealTexture;
        else if (currentEffect == FaceEffect.RealTexture)
            currentEffect = FaceEffect.Nothing;

       


    }

    public void onButtonPlay()
    {
        if (!isTracking)
        {
            stopTrackButton = false;
            trackingButton.image.overrideSprite = trackPause;
            isTracking = true;
        }
        else
        {
            stopTrackButton = true;
            trackingButton.image.overrideSprite = trackPlay;
            isTracking = false;
        }
    }

    

    
    #endregion


    /// <summary>
    /// Initialize tracker with maximum number of faces - MAX_FACES.
    /// Additionally, depending on a platform set an appropriate shader.
    /// </summary>
    /// <param name="config">Tracker configuration path and name.</param>
    bool InitializeTracker(string config)
    {
        Debug.Log("Visage Tracker: Initializing tracker with config: '" + config + "'");





        Shader shader = Shader.Find("Custom/RGBATex");
        CameraViewMaterial.shader = shader;


        VisageTrackerNative._initTracker(config, MAX_FACES);
        return true;

    }

    #region Callback Function for WEBGL

    public void CallbackInitTracker()
    {
        Debug.Log("TrackerInited");
        trackerInited = true;
    }

    public void OnSuccessCallbackCamera()
    {
        Debug.Log("CameraSuccess");
        camInited = true;
    }

    public void OnErrorCallbackCamera()
    {
        Debug.Log("CameraError");
    }

    #endregion

    /// <summary>
    /// Update Unity texture with frame data from native camera.
    /// </summary>
    void RefreshImage()
    {
        // Initialize texture
        if (texture == null && isTracking && ImageWidth > 0)
        {
            TexWidth = Convert.ToInt32(Math.Pow(2.0, Math.Ceiling(Math.Log(ImageWidth) / Math.Log(2.0))));
            TexHeight = Convert.ToInt32(Math.Pow(2.0, Math.Ceiling(Math.Log(ImageHeight) / Math.Log(2.0))));
            texture = new Texture2D(TexWidth, TexHeight, TexFormat, false);

            var cols = texture.GetPixels32();
            for (var i = 0; i < cols.Length; i++)
                cols[i] = Color.black;

            texture.SetPixels32(cols);
            texture.Apply(false);

            CameraViewMaterial.SetTexture("_MainTex", texture);


            // "pin" the pixel array in memory, so we can pass direct pointer to it's data to the plugin,
            // without costly marshaling of array of structures.
            texturePixels = ((Texture2D)texture).GetPixels32(0);
            texturePixelsHandle = GCHandle.Alloc(texturePixels, GCHandleType.Pinned);
        }

        if (texture != null && isTracking && TrackerStatus[0] != (int)TrackStatus.OFF)
        {

            // send memory address of textures' pixel data to VisageTrackerUnityPlugin
            VisageTrackerNative._setFrameData(texturePixelsHandle.AddrOfPinnedObject());
            ((Texture2D)texture).SetPixels32(texturePixels, 0);
            ((Texture2D)texture).Apply();

        }
    }


    /// <summary>
    /// Get current device orientation.
    /// </summary>
    /// <returns>Returns an integer:
    /// <list type="bullet">
    /// <item><term>0 : DeviceOrientation.Portrait</term></item>
    /// <item><term>1 : DeviceOrientation.LandscapeRight</term></item>
    /// <item><term>2 : DeviceOrientation.PortraitUpsideDown</term></item>
    /// <item><term>3 : DeviceOrientation.LandscapeLeft</term></item>
    /// </list>
    /// </returns>
    int GetDeviceOrientation()
    {
        int devOrientation;


        if (Input.deviceOrientation == DeviceOrientation.Portrait)
            devOrientation = 0;
        else if (Input.deviceOrientation == DeviceOrientation.PortraitUpsideDown)
            devOrientation = 2;
        else if (Input.deviceOrientation == DeviceOrientation.LandscapeLeft)
            devOrientation = 3;
        else if (Input.deviceOrientation == DeviceOrientation.LandscapeRight)
            devOrientation = 1;
        else if (Input.deviceOrientation == DeviceOrientation.FaceUp)
            devOrientation = Orientation;
        else if (Input.deviceOrientation == DeviceOrientation.Unknown)
            devOrientation = Orientation;
        else
            devOrientation = 0;


        return devOrientation;
    }


    /// <summary> 
    /// Open camera from native code. 
    /// </summary>
    /// <param name="orientation">Current device orientation:
    /// <list type="bullet">
    /// <item><term>0 : DeviceOrientation.Portrait</term></item>
    /// <item><term>1 : DeviceOrientation.LandscapeRight</term></item>
    /// <item><term>2 : DeviceOrientation.PortraitUpsideDown</term></item>
    /// <item><term>3 : DeviceOrientation.LandscapeLeft</term></item>
    /// </list>
    /// </param>
    /// <param name="camDeviceId">ID of the camera device.</param>
    /// <param name="width">Desired width in pixels (pass -1 for default 800).</param>
    /// <param name="height">Desired width in pixels (pass -1 for default 600).</param>
    /// <param name="isMirrored">true if frame is to be mirrored, false otherwise.</param>
    bool OpenCamera(int orientation, int cameraDeviceId, int width, int height, int isMirrored)
    {

        VisageTrackerNative._openCamera(orientation, cameraDeviceId, width, height);
        
        return true;

    }

    /// <summary>
    /// Apply data from the tracker to controllable objects (glasses, tiger mesh).
    /// </summary>
    private void UpdateControllableObjects(int faceIndex)
    {
        

        TriangleNumber = VisageTrackerNative._getFaceModelTriangleCount();
        VertexNumber = VisageTrackerNative._getFaceModelVertexCount();
        meshFilter.mesh.Clear();



        // update translation and rotation

        VisageTrackerNative._getHeadTranslation(translation, faceIndex);
        VisageTrackerNative._getHeadRotation(rotation, faceIndex);
        //
        Translation[faceIndex].x = translation[0];
        Translation[faceIndex].y = translation[1];
        Translation[faceIndex].z = translation[2];

        Rotation[faceIndex].x = rotation[0];

        if ((Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer) && isMirrored == 1)
        {
            Rotation[faceIndex].y = -rotation[1];
            Rotation[faceIndex].z = -rotation[2];
        }
        else
        {
            Rotation[faceIndex].y = rotation[1];
            Rotation[faceIndex].z = rotation[2];
        }

        Transform3DData(faceIndex);
        



        VisageTrackerNative._getFaceModelVertices(vertices, faceIndex);
        VisageTrackerNative._getFaceModelTriangles(triangles, faceIndex);





        if (faceIndex < ControllableObjectsTiger.Length)
        {
            // Get mesh vertices
            if (Vertices[faceIndex] == null || Vertices[faceIndex].Length != VertexNumber)
                Vertices[faceIndex] = new Vector3[VertexNumber];




            for (int j = 0; j < VertexNumber; j++)
            {
                Vertices[faceIndex][j] = new Vector3(vertices[j * 3 + 0], vertices[j * 3 + 1], vertices[j * 3 + 2]);

            }


            Vector3[] mv = GetMeanFaceVertices();


            Vector3[] resultVertices = new Vector3[VertexNumber];

            for (int j = 0; j < VertexNumber; j++)
            {
                resultVertices[j] = Vertices[faceIndex][j];

            }


            if (currentEffect == FaceEffect.Nothing)
            {
                for (int j = 0; j < VertexNumber; j++)
                {
                    resultVertices[j].z = Vertices[faceIndex][j].z + 1000f;

                }
            }

            float empCar;
            float empEye;
            float empNoseWidth;
            float empNoseLenght;
            float empLip;

            if (currentEffect == FaceEffect.Normal)
            {
                empCar = 0f;
                empEye = 0f;
                empNoseWidth = 0f;
                empNoseLenght = 0f;
                empLip = 0f;

            }
            else
            {
                empCar = 1.4f;
                empEye = 5f;
                empNoseWidth = 6f;
                empNoseLenght = 1.5f;
                empLip = 3f;

            }
            //define emphasis 



            //compare face vertices with mean face vertices
            int[] mouthInner = new int[] { 191, 189, 259, 165, 316, 321, 209, 190, 188, 322, 327, 306, 166, 134 };
            //int[] faceShape = new int[] { };


            for (int j = 0; j < VertexNumber; j++)
            {

                if (mouthInner.Contains(j))
                    continue;
                float diffX = mv[j].x - Vertices[faceIndex][j].x;
                float diffY = mv[j].y - Vertices[faceIndex][j].y;
                float diffZ = mv[j].z - Vertices[faceIndex][j].z;

                if (diffX >= 0)
                {
                    resultVertices[j].x -= diffX * empCar;
                }
                else
                {
                    resultVertices[j].x += -diffX * empCar;
                }
                if (diffY >= 0)
                {
                    resultVertices[j].y -= diffY * empCar;
                }
                else
                {
                    resultVertices[j].y += -diffY * empCar;
                }
                if (diffZ >= 0)
                {
                    resultVertices[j].z -= diffZ * empCar;
                }
                else
                {
                    resultVertices[j].z += -diffZ * empCar;
                }

            }


            //translatacija osnovnih struktura- lijevo oko, desno oko, nos, usta

            //lijevo oko



            float distanceLeftEye_MeanX = Math.Abs(mv[178].x - mv[341].x);
            float distanceLeftEye_RealX = Math.Abs(Vertices[faceIndex][178].x - Vertices[faceIndex][341].x);

            float distanceLeftEye_MeanY = Math.Abs(mv[332].y - mv[193].y);
            float distanceLeftEye_RealY = Math.Abs(Vertices[faceIndex][332].y - Vertices[faceIndex][193].y);

            int[] leftEye = new int[] { 28, 111, 113, 179, 221, 220, 305, 332, 299, 177, 178, 300, 110, 112, 180, 219, 273, 328, 272, 108, 222 };

            for (int j = 0; j < leftEye.Length; j++)
            {
                int pos = leftEye[j];

                if (distanceLeftEye_RealX < distanceLeftEye_MeanX)
                {

                    resultVertices[pos].x += (distanceLeftEye_MeanX - distanceLeftEye_RealX) * empEye;
                }
                else
                {

                    resultVertices[pos].x -= (distanceLeftEye_RealX - distanceLeftEye_MeanX) * empEye;
                }

                if (distanceLeftEye_RealY < distanceLeftEye_MeanY)
                {
                    resultVertices[pos].y += (distanceLeftEye_MeanY - distanceLeftEye_RealY) * empEye / 5;
                }
                else
                {
                    resultVertices[pos].y -= (distanceLeftEye_RealY - distanceLeftEye_MeanY) * empEye / 5;
                }

            }


            //desno oko
            float distanceRightEye_MeanX = Math.Abs(mv[62].x - mv[341].x);
            float distanceRightEye_RealX = Math.Abs(Vertices[faceIndex][62].x - Vertices[faceIndex][341].x);

            float distanceRightEye_MeanY = Math.Abs(mv[311].y - mv[193].y);
            float distanceRightEye_RealY = Math.Abs(Vertices[faceIndex][311].y - Vertices[faceIndex][193].y);

            int[] rightEye = new int[] { 250, 200, 155, 61, 62, 53, 52, 311, 151, 247, 248, 249, 199, 154, 63, 51, 50, 308, 150, 246, 5 };

            for (int j = 0; j < rightEye.Length; j++)
            {
                int pos = rightEye[j];

                if (distanceRightEye_RealX < distanceRightEye_MeanX)
                {

                    resultVertices[pos].x -= (distanceRightEye_MeanX - distanceRightEye_RealX) * empEye;
                }
                else
                {

                    resultVertices[pos].x += (distanceRightEye_RealX - distanceRightEye_MeanX) * empEye;
                }

                if (distanceRightEye_RealY < distanceRightEye_MeanY)
                {
                    resultVertices[pos].y += (distanceRightEye_MeanY - distanceRightEye_RealY) * empEye / 5;
                }
                else
                {
                    resultVertices[pos].y -= (distanceRightEye_RealY - distanceRightEye_MeanY) * empEye / 5;
                }

            }

            //nos-širina
            int[] noseWidthLeft = new int[] { 218, 275, 280, 237, 268, 274, 279, 235, 286, 238, 239, 269, 233, 232 };
            int[] noseWidthRight = new int[] { 89, 282, 295, 262, 294, 296, 261, 297, 263, 264, 80, 79, 260, 91 };

            float distanceNoseWidthLeftMean = Math.Abs(mv[274].x - mv[234].x);
            float distanceNoseWidthLeftReal = Math.Abs(Vertices[faceIndex][274].x - Vertices[faceIndex][234].x);



            float distanceNoseWidthRightMean = Math.Abs(mv[294].x - mv[234].x);
            float distanceNoseWidthRightReal = Math.Abs(Vertices[faceIndex][294].x - Vertices[faceIndex][234].x);



            for (int j = 0; j < noseWidthLeft.Length; j++)
            {
                int pos = noseWidthLeft[j];

                if (distanceNoseWidthLeftReal < distanceNoseWidthLeftMean)
                {

                    resultVertices[pos].x += (distanceNoseWidthLeftMean - distanceNoseWidthLeftReal) * empNoseWidth;
                }
                else
                {

                    resultVertices[pos].x -= (distanceNoseWidthLeftReal - distanceNoseWidthLeftMean) * empNoseWidth;
                }
            }
            for (int j = 0; j < noseWidthRight.Length; j++)
            {
                int pos = noseWidthRight[j];

                if (distanceNoseWidthRightReal < distanceNoseWidthRightMean)
                {

                    resultVertices[pos].x -= (distanceNoseWidthRightMean - distanceNoseWidthRightReal) * empNoseWidth;
                }
                else
                {

                    resultVertices[pos].x += (distanceNoseWidthRightReal - distanceNoseWidthRightMean) * empNoseWidth;
                }
            }


            //nos-visina
            int[] noseLenght = new int[] { 145,231,169,96,78,233,232,
                284,260,79,269,80,274,286,238,239,283,264,263,297,294,279,235,234,261,296,268,282,280,
                237,236,262,295,89,285,90,88,275};
            float distanceNoseLenghtMean = Math.Abs(mv[93].y - mv[234].y);
            float distanceNoseLenghtReal = Math.Abs(Vertices[faceIndex][93].y - Vertices[faceIndex][234].y);
            for (int j = 0; j < noseLenght.Length; j++)
            {
                int pos = noseLenght[j];

                if (distanceNoseLenghtReal < distanceNoseLenghtMean)
                {

                    resultVertices[pos].y += (distanceNoseLenghtMean - distanceNoseLenghtReal) * empNoseLenght;
                }
                else
                {

                    resultVertices[pos].y -= (distanceNoseLenghtReal - distanceNoseLenghtMean) * empNoseLenght;
                }
            }

            //debljina usana
            int[] upperLip = new int[] { 23, 175, 334, 138, 139, 168, 307, 245, 0, 167 };
            float distanceUpperLipMean = Math.Abs(mv[139].y - mv[191].y);
            float distanceUpperLipReal = Math.Abs(Vertices[faceIndex][139].y - Vertices[faceIndex][191].y);
            for (int j = 0; j < upperLip.Length; j++)
            {
                int pos = upperLip[j];

                if (distanceUpperLipReal > distanceUpperLipMean)
                {

                    resultVertices[pos].y -= (distanceUpperLipMean - distanceUpperLipReal) * empLip;
                }
                else
                {

                    resultVertices[pos].y += (distanceUpperLipReal - distanceUpperLipMean) * empLip;
                }
            }



            int[] lowerLip = new int[] { 172, 171, 324, 131, 130, 82, 319, 84, 86 };
            float distanceLowerLipMean = Math.Abs(mv[189].y - mv[130].y);
            float distanceLowerLipReal = Math.Abs(Vertices[faceIndex][189].y - Vertices[faceIndex][130].y);
            for (int j = 0; j < lowerLip.Length; j++)
            {
                int pos = lowerLip[j];

                if (distanceLowerLipReal > distanceLowerLipMean)
                {

                    resultVertices[pos].y += (distanceLowerLipMean - distanceLowerLipReal) * empLip;
                }
                else
                {

                    resultVertices[pos].y -= (distanceLowerLipReal - distanceLowerLipMean) * empLip;
                }
            }


            // Get mesh triangles
            if (Triangles.Length != TriangleNumber)
                Triangles = new int[TriangleNumber * 3];

            for (int j = 0; j < TriangleNumber * 3; j++)
            {
                Triangles[j] = triangles[j];
            }

            // Get mesh texture coordinates
            if (TexCoords.Length != VertexNumber)
                TexCoords = new Vector2[VertexNumber];


            if (currentEffect == FaceEffect.RealTexture) { 

            VisageTrackerNative._getFaceModelTextureCoords(texCoords, faceIndex);


            for (int j = 0; j < VertexNumber; j++)
            {
                TexCoords[j] = new Vector2(texCoords[j * 2 + 0], texCoords[j * 2 + 1]);

            }

            
            MeshRenderer mat = ControllableObjectsTiger[faceIndex].GetComponent<MeshRenderer>();
            mat.material.mainTexture = texture;
                mat.material.shader = Shader.Find("Unlit/Texture");
                
        }
            else
            {
                for (int j = 0; j < VertexNumber; j++)
                {
                    TexCoords[j] = new Vector2(modelTexCoords[j].x, modelTexCoords[j].y);

                }

                MeshRenderer mat = ControllableObjectsTiger[faceIndex].GetComponent<MeshRenderer>();
                mat.material.mainTexture = (Texture2D)Resources.Load("maskaproba6");
               

                mat.material.shader = Shader.Find("Unlit/Texture");
            }
           


            MeshFilter meshFilter = ControllableObjectsTiger[faceIndex].GetComponent<MeshFilter>();
            meshFilter.mesh.vertices = resultVertices; //needs to be obtained for each face
            meshFilter.mesh.triangles = Triangles; //not changing 
            meshFilter.mesh.uv = TexCoords; // tiger texture coordinates
            meshFilter.mesh.uv2 = TexCoords; // tiger texture coordinates
            meshFilter.mesh.Optimize();
            
            meshFilter.mesh.RecalculateNormals();
            meshFilter.mesh.RecalculateBounds();
        }

        // Update mesh position
        ControllableObjectsTiger[faceIndex].transform.position = startingPositionsTiger[faceIndex] + Translation[faceIndex];

        ControllableObjectsTiger[faceIndex].transform.rotation = Quaternion.Euler(startingRotationsTiger[faceIndex] + Rotation[faceIndex]);

        if ((Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer) && isMirrored == 1)
        {
            ControllableObjectsTiger[faceIndex].transform.position = Vector3.Scale(ControllableObjectsTiger[faceIndex].transform.position, new Vector3(-1, 1, 1));
            ControllableObjectsTiger[faceIndex].transform.localScale = new Vector3(1, 1, 1);

        }




    }

    /// <summary>
    /// Reset data in controllable objects (glasses, tiger mesh) when tracker is not tracking.
    /// </summary>
    public void ResetControllableObjects(int faceIndex)
    {
       // ControllableObjectsTiger[faceIndex].transform.position = startingPositionsTiger[faceIndex] + new Vector3(-10000, -10000, -10000);
        //ControllableObjectsTiger[faceIndex].transform.rotation = Quaternion.Euler(startingRotationsTiger[faceIndex] + new Vector3(0, 0, 0));
    }

    /// <summary>
    /// Helper function for transforming data obtained from tracker
    /// </summary>
    public void Transform3DData(int i)
    {
        Translation[i].x *= (-1);
        Rotation[i].x = 180.0f * Rotation[i].x / 3.14f;
        Rotation[i].y += 3.14f;
        Rotation[i].y = 180.0f * (-Rotation[i].y) / 3.14f;
        Rotation[i].z = 180.0f * (-Rotation[i].z) / 3.14f;
    }

    /// <summary>
    /// Initialize arrays used for controllable objects (glasses, tiger texture)
    /// </summary>
    void InitializeContainers()
    {

        // Initialize translation and rotation arrays
        for (int i = 0; i < MAX_FACES; i++)
        {
            Translation[i] = new Vector3(0, 0, -1000);
            Rotation[i] = new Vector3(0, 0, -1000);
        }

        // Initialize arrays for controllable objects


        startingPositionsTiger = new Vector3[ControllableObjectsTiger.Length];
        startingRotationsTiger = new Vector3[ControllableObjectsTiger.Length];




        for (int i = 0; i < ControllableObjectsTiger.Length; i++)
        {
            startingPositionsTiger[i] = ControllableObjectsTiger[i].transform.position;
            startingRotationsTiger[i] = ControllableObjectsTiger[i].transform.rotation.eulerAngles;
        }
    }


    /// <summary>
    /// Load sprites for GUI play button
    /// </summary>
    private void LoadButtonSprites()
    {
        trackPlay = Resources.Load<Sprite>("play");
        trackPause = Resources.Load<Sprite>("pause");
    }


    /// <summary>
    /// Loads static texture coordinates from the plugin.
    /// </summary>
    /// <returns>Returns true on successful load, false otherwise.</returns>
    bool GetTextureCoordinates(out Vector2[] texCoords)
    {
        int texCoordsNumber;
        float[] buffer = new float[1024];
        VisageTrackerNative._getTexCoordsStatic(buffer, out texCoordsNumber);

        texCoords = new Vector2[texCoordsNumber / 2];
        for (int i = 0; i < texCoordsNumber / 2; i++)
        {
            texCoords[i] = new Vector2(buffer[i * 2], buffer[i * 2 + 1]);

        }

        return texCoordsNumber > 0;
    }

    Vector3[] GetMeanFaceVertices()
    {
        Vector3[] meanVertices = new Vector3[357];
        string path = Application.streamingAssetsPath + "/" + "/Visage Tracker/mean/mean.txt";
        var lines = File.ReadLines(path);
        int i = 0;
        foreach(String line in lines)
        {
           
            string[] a = line.Split(';');
            
            meanVertices[i] = new Vector3(float.Parse(a[0]), float.Parse(a[1]), float.Parse(a[2]));
            i++;



        }


        return meanVertices;
    }

}