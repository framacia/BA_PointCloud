using BAPointCloudRenderer.CloudData;
using BAPointCloudRenderer.Loading;
using System;
using UnityEngine;
using UnityEditor;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using UnityEngine.Networking;

namespace BAPointCloudRenderer.CloudController
{
    /// <summary>
    /// Use this script to load a single PointCloud from a directory.
    ///
    /// Streaming Assets support provided by Pablo Vidaurre
    /// </summary>
    public class PointCloudLoader : MonoBehaviour
    {
        /// <summary>
        /// Path to the folder which contains the cloud.js file or URL to download the cloud from. In the latter case, it will be downloaded to a /temp folder
        /// </summary>
        public string cloudPath;

        /// <summary>
        /// When true, the cloudPath is relative to the streaming assets directory
        /// </summary>
        public bool streamingAssetsAsRoot = false;

        /// <summary>
        /// The PointSetController to use
        /// </summary>
        public AbstractPointCloudSet setController;

        /// <summary>
        /// True if the point cloud should be loaded when the behaviour is started. Otherwise the point cloud is loaded when LoadPointCloud is loaded.
        /// </summary>
        public bool loadOnStart = true;

        private Node rootNode;

        private void Awake()
        {
            if (streamingAssetsAsRoot) cloudPath = Application.streamingAssetsPath + "/" + cloudPath;
        }

        void Start()
        {
            if (loadOnStart)
            {
                LoadPointCloud();
            }
        }

        private void LoadHierarchyOld()
        {
            try
            {
                if (!cloudPath.EndsWith("/"))
                {
                    cloudPath = cloudPath + "/";
                }

                PointCloudMetaData metaData = CloudLoader.LoadMetaData(cloudPath, false);

                setController.UpdateBoundingBox(this, metaData.boundingBox_transformed,
                    metaData.tightBoundingBox_transformed);

                rootNode = CloudLoader.LoadHierarchyOnly(metaData);

                setController.AddRootNode(this, rootNode, metaData);
            }
            catch (System.IO.FileNotFoundException ex)
            {
                Debug.LogError("Could not find file: " + ex.FileName);
            }
            catch (System.IO.DirectoryNotFoundException ex)
            {
                Debug.LogError("Could not find directory: " + ex.Message);
            }
            catch (System.Net.WebException ex)
            {
                Debug.LogError("Could not access web address. " + ex.Message);
            }
            catch (Exception ex)
            {
                //Debug.LogError(ex + Thread.CurrentThread.Name);
            }
        }

        private IEnumerator LoadHierarchyCR()
        {
            if (!cloudPath.EndsWith("/"))
            {
                cloudPath = cloudPath + "/";
            }

            PointCloudMetaData metaData = null;

            //PointCloudMetaData metaData = CloudLoader.LoadMetaData(cloudPath, false);

            var waitForMetaData =
                StartCoroutine(LoadMetaDataCR(cloudPath, (loadedMetaData) => { metaData = loadedMetaData; }, false));

            yield return waitForMetaData;

            Debug.Log("POINTSFOUND: " + metaData.points);

            setController.UpdateBoundingBox(this, metaData.boundingBox_transformed,
                metaData.tightBoundingBox_transformed);
            // wait here so we are sure the bounding box is updated correctly
            yield return new WaitForSeconds(0.5f);

            //rootNode = CloudLoader.LoadHierarchyOnly(metaData);
            var waitForNode = StartCoroutine(LoadHierarchyOnly(metaData, (node) => { rootNode = node; }));
            
            yield return waitForNode;

            setController.AddRootNode(this, rootNode, metaData);
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
                PointCloudMetaData metaData = PointCloudMetaDataReader.ReadFromJson(jsonfile, moveToOrigin);
                metaData.cloudPath = cloudPath + "/";
                metaData.cloudName = cloudPath.Substring(0, cloudPath.Length - 1)
                    .Substring(cloudPath.Substring(0, cloudPath.Length - 1).LastIndexOf("\\") + 1);

                callback.Invoke(metaData);
            }
        }

        public IEnumerator LoadHierarchyOnly(PointCloudMetaData metaData, Action<Node> callback)
        {
            string dataRPath = metaData.cloudPath + metaData.octreeDir + "\\r\\";
            Node rootNode = new Node("", metaData, metaData.tightBoundingBox_transformed, null);
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
                        Node child = new Node(n.Name + j, metaData, CloudLoader.CalculateBoundingBox(n.BoundingBox, j), n);

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
        
        /// <summary>
        /// Starts loading the point cloud. When the hierarchy is loaded it is registered at the corresponding point cloud set
        /// </summary>
        public void LoadPointCloud()
        {
            if (rootNode == null && setController != null && cloudPath != null)
            {
                setController.RegisterController(this);
                // Thread thread = new Thread(LoadHierarchy);
                // thread.Name = "Loader for " + cloudPath;
                // thread.Start();
                StartCoroutine(LoadHierarchyCR());
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
            rootNode = null;
            return true;
        }
    }
}