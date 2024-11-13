using BAPointCloudRenderer.CloudController;
using BAPointCloudRenderer.CloudData;
using BAPointCloudRenderer.ObjectCreation;
using System.Collections.Generic;
using System.Collections;
using System.Threading;
using UnityEngine;

namespace BAPointCloudRenderer.Loading
{
    /// <summary>
    /// The multithreaded Real-Time-Renderer as described in the Bachelor Thesis in chapter 3.2.2 - 3.2.7
    /// </summary>
    class V2RendererCR : AbstractRenderer
    {
        private bool
            paused = false; //true, iff everything should be stopped (the point loading will stop and every method will not do anything anymore)

        private V2TraversalThreadCR traversalThread;
        private V2LoadingThreadCR loadingThread;
        private V2CacheCR cache;
        private List<Node> rootNodes; //List of root nodes of the point clouds
        private Thread unityThread = null;
        private AbstractPointCloudSetCR _pointCloudSet;

        private MeshConfiguration config;
        private uint renderingpointcount;

        //Camera Info
        private Camera camera;

        private Queue<Node> toRender;
        private Queue<Node> toDelete;
        private Queue<Node> toDeleteExternal; //Nodes that have been scheduled for removal via removeRoot

        /// <summary>
        /// Creates a new V2Renderer and starts all the threads
        /// </summary>
        /// <param name="minNodeSize">Minimum Node Size</param>
        /// <param name="pointBudget">Point Budget</param>
        /// <param name="nodesLoadedPerFrame">Maximum number of nodes loaded per frame</param>
        /// <param name="nodesGOsperFrame">Maximum number of nodes for which GameObjects should be created per frame</param>
        /// <param name="camera">User Camera</param>
        /// <param name="config">MeshConfiguration, defining how the points should be rendered</param>
        /// <param name="cacheSize">Size of cache in points</param>
        public V2RendererCR(AbstractPointCloudSetCR pointCloudSet, int minNodeSize, uint pointBudget,
            uint nodesLoadedPerFrame, uint nodesGOsperFrame, Camera camera, MeshConfiguration config, uint cacheSize)
        {
            _pointCloudSet = pointCloudSet;
            rootNodes = new List<Node>();
            this.camera = camera;
            this.config = config;
            cache = new V2CacheCR(cacheSize);
            loadingThread = new V2LoadingThreadCR(cache);
            loadingThread.Start(_pointCloudSet, _pointCloudSet.Loader);

            traversalThread = new V2TraversalThreadCR(this, loadingThread, rootNodes, minNodeSize, pointBudget,
                nodesLoadedPerFrame, nodesGOsperFrame, cache);
            traversalThread.Start(_pointCloudSet);

            toDeleteExternal = new Queue<Node>();
        }


        /// <summary>
        /// Registers the root node of a point cloud in the renderer.
        /// </summary>
        /// <param name="rootNode">not null</param>
        public void AddRootNode(Node rootNode)
        {
            rootNodes.Add(rootNode);
        }

        /// <summary>
        /// Removes the root node of a point cloud from the renderer. The node will not be rendered any more.
        /// This has to be called from the main thread!
        /// </summary>
        /// <param name="rootNode">not null</param>
        public void RemoveRootNode(Node rootNode)
        {
            toDeleteExternal.Enqueue(rootNode);
        }

        /// <summary>
        /// Returns how many root nodes have been added
        /// </summary>
        public int GetRootNodeCount()
        {
            return rootNodes.Count - toDeleteExternal.Count;
        }

        /// <summary>
        /// True, if ShutDown() has not been called yet
        /// </summary>
        public bool IsRunning()
        {
            return !paused;
        }


        /// <summary>
        /// Gives the current camera data to the traversal thread and updates the GameObjects. Called from the MainThread. As described in the Bachelor Thesis in chapter 3.1.3 "Main Thread"
        /// </summary>
        public void Update()
        {
            if (Traversed)
            {
                unityThread = Thread.CurrentThread;
                if (paused) return;
                //Set new Camera Data
                traversalThread.SetNextCameraData(camera.transform.position, camera.transform.forward,
                    GeometryUtility.CalculateFrustumPlanes(camera), camera.pixelRect.height, camera.fieldOfView);

                //Update GameObjects
                Queue<Node> toRender;
                Queue<Node> toDelete;

                toRender = this.toRender;
                toDelete = this.toDelete;
                this.toRender = null;
                this.toDelete = null;

                if (toRender == null)
                {
                    return;
                }

                while (toDelete.Count != 0)
                {
                    Node n = toDelete.Dequeue();

                    if (n.HasGameObjects())
                    {
                        n.RemoveGameObjects(config);
                        cache.Insert(n);
                    }
                }

                while (toRender.Count != 0)
                {
                    Node n = toRender.Dequeue();
                    if (n.HasPointsToRender() && (n.Parent == null || n.Parent.HasGameObjects()))
                    {
                        n.CreateGameObjects(config, null);
                    }
                }

                while (toDeleteExternal.Count != 0)
                {
                    Node rootNode = toDeleteExternal.Dequeue();
                    rootNodes.Remove(rootNode);
                    Queue<Node> toRemove = new Queue<Node>();
                    toRemove.Enqueue(rootNode);
                    while (toRemove.Count != 0)
                    {
                        Node n = toRemove.Dequeue();
                        cache.Withdraw(n);
                        if (n.HasGameObjects())
                        {
                            n.RemoveGameObjects(config);
                        }

                        if (n.HasPointsToRender())
                        {
                            n.ForgetPoints();
                            foreach (Node child in n)
                            {
                                toRemove.Enqueue(child);
                            }
                        }
                    }
                }

                Traversed = false;
            }

            traversalThread.Wait = false;
            //Notify Traversal Thread
            /*lock (traversalThread) {
                Monitor.PulseAll(traversalThread);
            }*/
        }

        public bool Traversed = true;

        /// <summary>
        /// Stops the rendering process and all threads
        /// Must be called from the main thread!
        /// </summary>
        public void ShutDown()
        {
            if (unityThread != null && Thread.CurrentThread != unityThread)
            {
                throw new System.Exception("ShutDown() has to be called from the Unity Main Thread!");
            }

            Pause();
            foreach (Node node in rootNodes)
            {
                node.RemoveAllGameObjects(config);
            }
        }

        /// <summary>
        /// Pauses the updating of the rendering.
        /// </summary>
        public void Pause()
        {
            paused = true;
            traversalThread.Stop();
            /*lock (traversalThread) {
                Monitor.PulseAll(traversalThread);
            }*/
            loadingThread.Stop();
        }

        /// <summary>
        /// Continues the rendering after pausing
        /// </summary>
        public void Continue()
        {
            loadingThread.Start(_pointCloudSet, _pointCloudSet.Loader);
            traversalThread.Start(_pointCloudSet);
            paused = false;
        }

        /// <summary>
        /// Pauses the rendering and hides all visible point clouds.
        /// </summary>
        public void Hide()
        {
            Pause();
            foreach (Node node in rootNodes)
            {
                node.DeactivateAllGameObjects();
            }
        }

        /// <summary>
        /// Continues the rendering and displays all visible point clouds after them being hidden via hide.
        /// </summary>
        public void Display()
        {
            foreach (Node node in rootNodes)
            {
                node.ReactivateAllGameObjects();
            }

            Continue();
        }

        /// <summary>
        /// Returns the current PointCount, so how many points are loaded / visible
        /// </summary>
        public uint GetPointCount()
        {
            return renderingpointcount;
        }

        /// <summary>
        /// Sets the new GO-update-queues. Called from the TraversalThread.
        /// </summary>
        public void SetQueues(Queue<Node> toRender, Queue<Node> toDelete, uint pointcount)
        {
            this.toRender = toRender;
            this.toDelete = toDelete;
            this.renderingpointcount = pointcount;
        }
    }
}