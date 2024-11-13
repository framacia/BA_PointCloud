using BAPointCloudRenderer.CloudData;
using BAPointCloudRenderer.Loading;
using System;
using System.Threading;
using System.Collections;
using UnityEngine;
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEngine.Networking;


namespace BAPointCloudRenderer.CloudController
{
    /* While PointCloudLoaderController will load the complete file as one and render the comlete one,
     * the DynamicLoaderController will first only load the hierarchy. It can be given registered at a PointCloudSetController to render it.
     */
    /// <summary>
    /// Use this script to load a single PointCloud from a directory.
    /// </summary>
    public class PointCloudLoaderCR : MonoBehaviour
    {
        /// <summary>
        /// Path to the folder which contains the cloud.js file
        /// </summary>
        public string cloudPath;

        /// <summary>
        /// The PointSetController to use
        /// </summary>
        public AbstractPointCloudSetCR setController;

        /// <summary>
        /// True if the point cloud should be loaded when the behaviour is started. Otherwise the point cloud is loaded when LoadPointCloud is loaded.
        /// </summary>
        public bool loadOnStart = true;

        public GameObject LoadVisual;
        private Node rootNode;

        private void Awake()
        {
            setController.Loader = this;
        }

        void Start()
        {
            if (loadOnStart)
            {
                LoadPointCloudCR();
            }
        }

        /// <summary>
        /// Removes the point cloud from the scene. Should only be called from the main thread!
        /// </summary>
        /// <returns>True if the cloud was removed. False, when the cloud hasn't even been loaded yet.</returns>
        public bool RemovePointCloud()
        {
            if (rootNode == null)
            {
                return false;
            }

            setController.RemoveRootNode(this, rootNode);
            return true;
        }


        private IEnumerator LoadHierarchyCR()
        {
            PointCloudMetaData metaData = null;
            string path;
#if UNITY_EDITOR
            path = Application.streamingAssetsPath + "/" + cloudPath + "/";
#elif UNITY_WEBGL && !UNIY_EDITOR
            path = Application.streamingAssetsPath + "/" + cloudPath + "/";
#endif


            var waitForMetaData =
                StartCoroutine(LoadMetaDataCR(path, (loadedMetaData) => { metaData = loadedMetaData; }, false));

            yield return waitForMetaData;
            Debug.Log("POINTSFOUND: " + metaData.points);
            setController.UpdateBoundingBoxCR(this, metaData.boundingBox);
            // wait here so we are sure the bounding box is updated correctly
            yield return new WaitForSeconds(0.5f);
            Node rootNode = null;

            var waitForNode = StartCoroutine(LoadHierarchyOnly(metaData, (node) => { rootNode = node; }));

            yield return waitForNode;
            LoadVisual.SetActive(false);
            setController.AddRootNode(rootNode);
        }

        public void LoadPointCloudCR()
        {
            setController.RegisterController(this);
            StartCoroutine(LoadHierarchyCR());
        }

        /// <summary>
        /// Loads the meta data from the json-file in the given cloudpath. Attributes "cloudPath", and "cloudName" are set as well.
        /// </summary>
        /// <param name="cloudPath">Folderpath of the cloud</param>
        /// <param name="moveToOrigin">True, if the center of the cloud should be moved to the origin</param>
        public IEnumerator LoadMetaDataCR(string cloudPath, Action<PointCloudMetaData> callback,
            bool moveToOrigin = false)
        {
            string jsonfile;


            var www = UnityWebRequest.Get(cloudPath + "/cloud.js");

            yield return www.SendWebRequest();

            if (www.error != null)
            {
                Debug.Log("failed to load cloud path: " + cloudPath + "cloud.js");
            }
            else
            {
                jsonfile = www.downloadHandler.text;
                PointCloudMetaData metaData = PointCloudMetaData.ReadFromJson(jsonfile, moveToOrigin);
                metaData.cloudPath = cloudPath + "/";
                metaData.cloudName = cloudPath.Substring(0, cloudPath.Length - 1)
                    .Substring(cloudPath.Substring(0, cloudPath.Length - 1).LastIndexOf("\\") + 1);

                callback.Invoke(metaData);
            }
        }

        public IEnumerator LoadHierarchyOnly(PointCloudMetaData metaData, Action<Node> callback)
        {
            string dataRPath = metaData.cloudPath + metaData.octreeDir + "\\r\\";
            Node rootNode = new Node("", metaData, metaData.boundingBox, null);
            var waitFor = StartCoroutine(LoadHierarchy(dataRPath, metaData, rootNode));

            yield return waitFor;

            callback.Invoke(rootNode);
        }

        /* Loads the complete hierarchy of the given node. Creates all the children and their data. Points are not yet stored in there.
         * dataRPath is the path of the R-folder
         */
        private IEnumerator LoadHierarchy(string dataRPath, PointCloudMetaData metaData, Node root)
        {
            byte[] data = new byte[0];

            var waitForFile = StartCoroutine(FindAndLoadFile(dataRPath, metaData, root.Name, ".hrc",
                (loadedData) => { data = loadedData; }));
            yield return waitForFile;
            int nodeByteSize = 5;
            int numNodes = data.Length / nodeByteSize;
            int offset = 0;

            //Debug.Log("NUMNODES: " + numNodes);
            var nextNodes = new Queue<Node>();
            nextNodes.Enqueue(root);

            for (int i = 0; i < numNodes; i++)
            {
                Node n = nextNodes.Dequeue();
                byte configuration = data[offset];
                //uint pointcount = System.BitConverter.ToUInt32(data, offset + 1);
                //n.PointCount = pointcount; //TODO: Pointcount is wrong
                for (int j = 0; j < 8; j++)
                {
                    //check bits
                    if ((configuration & (1 << j)) != 0)
                    {
                        //This is done twice for some nodes
                        Node child = new Node(n.Name + j, metaData, CalculateBoundingBox(n.BoundingBox, j), n);

                        n.SetChild(j, child);
                        nextNodes.Enqueue(child);
                    }
                }

                offset += 5;
            }

            HashSet<Node> parentsOfNextNodes = new HashSet<Node>();

            while (nextNodes.Count != 0)
            {
                Node n = nextNodes.Dequeue().Parent;
                if (!parentsOfNextNodes.Contains(n))
                {
                    parentsOfNextNodes.Add(n);
                    var waitForNode = StartCoroutine(LoadHierarchy(dataRPath, metaData, n));
                    yield return new WaitForEndOfFrame();
                }
            }
        }

        private BoundingBox CalculateBoundingBox(BoundingBox parent, int index)
        {
            Vector3d min = parent.Min();
            Vector3d max = parent.Max();
            Vector3d size = parent.Size();
            //z and y are different here than in the sample-code because these coordinates are switched in unity
            if ((index & 2) != 0)
            {
                min.z += size.z / 2;
            }
            else
            {
                max.z -= size.z / 2;
            }

            if ((index & 1) != 0)
            {
                min.y += size.y / 2;
            }
            else
            {
                max.y -= size.y / 2;
            }

            if ((index & 4) != 0)
            {
                min.x += size.x / 2;
            }
            else
            {
                max.x -= size.x / 2;
            }

            return new BoundingBox(min, max);
        }

        private IEnumerator FindAndLoadFile(string dataRPath, PointCloudMetaData metaData, string id, string fileending,
            Action<byte[]> callback)
        {
            int levels = id.Length / metaData.hierarchyStepSize;
            var builder = new StringBuilder();
            builder.Append(dataRPath);

            for (int i = 0; i < levels; i++)
            {
                builder.Append(id.Substring(i * metaData.hierarchyStepSize, metaData.hierarchyStepSize) + "\\");
            }

            builder.Append("r" + id + fileending);

            var www = UnityWebRequest.Get(builder.ToString());

            yield return www.SendWebRequest();

            if (www.error != null)
            {
                Debug.Log("Failed to load file : " + builder.ToString());
            }
            else
            {
                callback.Invoke(www.downloadHandler.data);
            }
        }

        public IEnumerator LoadPointsForNode(Node node)
        {
            string dataRPath = node.MetaData.cloudPath + node.MetaData.octreeDir + "\\r\\";
            var waitForPoint = StartCoroutine(LoadPoints(dataRPath, node.MetaData, node));
            yield return waitForPoint;
        }

        private IEnumerator LoadPoints(string dataRPath, PointCloudMetaData metaData, Node node)
        {
            byte[] data = new byte[0];
            var waitForData = StartCoroutine(FindAndLoadFile(dataRPath, metaData, node.Name, ".bin",
                (newData) => { data = newData; }));

            yield return waitForData;

            int pointByteSize = metaData.pointByteSize;
            int numPoints = data.Length / pointByteSize;
            int offset = 0, toSetOff = 0;

            Vector3[] vertices = new Vector3[numPoints];
            Color[] colors = new Color[numPoints];
            //Read in data
            foreach (PointAttribute pointAttribute in metaData.pointAttributesList)
            {
                toSetOff = 0;
                if (pointAttribute.name.ToUpper().Equals(PointAttributes.POSITION_CARTESIAN) ||
                    pointAttribute.name.ToUpper().Equals(PointAttributes.POSITION))
                {
                    for (int i = 0; i < numPoints; i++)
                    {
                        //Reduction to single precision!
                        //Note: y and z are switched
                        float x = (float)(System.BitConverter.ToUInt32(data, offset + i * pointByteSize + 0) *
                                          metaData.scale3d.x);
                        float y = (float)(System.BitConverter.ToUInt32(data, offset + i * pointByteSize + 8) *
                                          metaData.scale3d.z);
                        float z = (float)(System.BitConverter.ToUInt32(data, offset + i * pointByteSize + 4) *
                                          metaData.scale3d.y);
                        vertices[i] = new Vector3(x, y, z);
                    }

                    toSetOff += 12;
                }
                else if (pointAttribute.name.ToUpper().Equals(PointAttributes.COLOR_PACKED))
                {
                    for (int i = 0; i < numPoints; i++)
                    {
                        byte r = data[offset + i * pointByteSize + 0];
                        byte g = data[offset + i * pointByteSize + 1];
                        byte b = data[offset + i * pointByteSize + 2];
                        colors[i] = new Color32(r, g, b, 255);
                    }

                    toSetOff += 3;
                }
                else if (pointAttribute.name.ToUpper().Equals(PointAttributes.RGBA) ||
                         pointAttribute.name.ToUpper().Equals(PointAttributes.RGB))
                {
                    if (metaData.version == "2.0")
                    {
                        CalculateRGBA(ref colors, ref offset, data, pointByteSize, numPoints,
                            pointAttribute.name.EndsWith("a"));
                    }
                    else
                    {
                        for (int i = 0; i < numPoints; i++)
                        {
                            byte r = data[offset + i * pointByteSize + 0];
                            byte g = data[offset + i * pointByteSize + 1];
                            byte b = data[offset + i * pointByteSize + 2];
                            byte a = data[offset + i * pointByteSize + 3];
                            colors[i] = new Color32(r, g, b, a);
                        }

                        toSetOff += 4;
                    }
                }

                /*
                 * for future reference.
                else if (metaData.version == 2.0)
                {
                    byte[] buff = new byte[numPoints * 4];
                    float[] f32 = new float[buff.Length / 4];

                    int taipsais = (pointAttribute as PointAttributeV2_0).typeSize;

                    double localOffset = 0;
                    double scale = 1;

                    // compute offset and scale to pack larger types into 32 bit floats
                    if ((pointAttribute as PointAttributeV2_0).typeSize > 4)
                    {
                        long[] aminmax = (pointAttribute as PointAttributeV2_0).range;
                        localOffset = aminmax[0];
                        scale = 1 / (aminmax[1] - aminmax[0]);
                        // this linq gymnastics is necessary for "future" types that have multiple values in minmax arrays. like "position".
                        scale = 1 / ((pointAttribute as PointAttributeV2_0).max.OrderByDescending(f => f).First() - (pointAttribute as PointAttributeV2_0).min.OrderBy(f => f).First());
                    }
                }
                */
                offset += metaData.version == "2.0" ? (pointAttribute as PointAttributeV2_0).byteSize : toSetOff;
            }

            node.SetPoints(vertices, colors);
        }

        internal void StartLoading()
        {
            LoadPointCloudCR();
        }
        
        private static void CalculateRGBA(ref Color[] colors, ref int offset, byte[] data, int pointByteSize, int numPoints, bool alpha)
        {
            int size = alpha ? 4 : 3;

            for (int j = 0; j < numPoints; j++)
            {
                int pointOffset = j * pointByteSize;

                UInt16 r = BitConverter.ToUInt16(data, pointOffset + offset + 0);
                UInt16 g = BitConverter.ToUInt16(data, pointOffset + offset + 2);
                UInt16 b = BitConverter.ToUInt16(data, pointOffset + offset + 4);

                // ~~~ !!! hardcoded alphaville !!! ~~~
                // although its called RGBA theres no alpha. so..
                colors[j] = new Color32((byte)(r >> 8), (byte)(g >> 8), (byte)(b >> 8), (byte)255);     //<< 8: Move from [0, 65535] to [0, 255]
            }
        }
    }
}