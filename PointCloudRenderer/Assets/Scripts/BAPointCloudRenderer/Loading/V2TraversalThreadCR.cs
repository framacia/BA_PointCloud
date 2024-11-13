using BAPointCloudRenderer.CloudData;
using BAPointCloudRenderer.DataStructures;
using System;
using System.Collections.Generic;
using System.Collections;
using System.Threading;
using UnityEngine;

namespace BAPointCloudRenderer.Loading {
    /// <summary>
    /// The traversal thread of the V2 Rendering System. Checks constantly, which nodes are visible and should be rendered and which not. Described in the Bachelor Thesis in chapter 3.2.4 "Traversal Thread".
    /// This is the place, where most of the magic happens.
    /// </summary>
    class V2TraversalThreadCR {

        private List<Node> rootNodes;
        private double minNodeSize; //Min projected node size
        private uint pointBudget;   //Point Budget
        private uint nodesLoadedPerFrame;
        private uint nodesGOsPerFrame;
        private bool running = true;
        public bool Wait = false;

        //Camera Data
        Vector3 cameraPosition;
        float screenHeight;
        float fieldOfView;
        Plane[] frustum;
        Vector3 camForward;

        private Queue<Node> toDelete;
        private Queue<Node> toRender;
        private HashSet<Node> visibleNodes;

        private V2RendererCR mainThread;
        private V2LoadingThreadCR loadingThread;
        private V2CacheCR cache;
        private MonoBehaviour _mono;
        private Thread thread;

        /// <summary>
        /// Creates the object, but does not start the thread yet
        /// </summary>
        public V2TraversalThreadCR(V2RendererCR mainThread, V2LoadingThreadCR loadingThread, List<Node> rootNodes, double minNodeSize, uint pointBudget, uint nodesLoadedPerFrame, uint nodesGOsPerFrame, V2CacheCR cache) {
            this.mainThread = mainThread;
            this.loadingThread = loadingThread;
            this.rootNodes = rootNodes;
            this.minNodeSize = minNodeSize;
            this.pointBudget = pointBudget;
            visibleNodes = new HashSet<Node>();
            this.cache = cache;
            this.nodesLoadedPerFrame = nodesLoadedPerFrame;
            this.nodesGOsPerFrame = nodesGOsPerFrame;
        }

        /// <summary>
        /// Starts the thread
        /// </summary>
        public void Start(MonoBehaviour mono) {
            running = true;
            _mono = mono;
            _mono.StartCoroutine(RunCR());
        }


        public IEnumerator RunCR()
        {
            while (running && !Wait)
            {
                toDelete = new Queue<Node>();
                toRender = new Queue<Node>();
                uint pointcount = TraverseAndBuildRenderingQueue();
                //Debug.Log("CURRENT POINTCOUNT: " + pointcount);
                mainThread.SetQueues(toRender, toDelete, pointcount);
                mainThread.Traversed = true;
                yield return new WaitForEndOfFrame();
            }
        }

        /// <summary>
        /// Sets the current camera data
        /// </summary>
        /// <param name="cameraPosition">Camera Position</param>
        /// <param name="camForward">Forward Vector</param>
        /// <param name="frustum">View Frustum</param>
        /// <param name="screenHeight">Screen Height</param>
        /// <param name="fieldOfView">Field of View</param>
        public void SetNextCameraData(Vector3 cameraPosition, Vector3 camForward, Plane[] frustum, float screenHeight, float fieldOfView) {
            this.cameraPosition = cameraPosition;
            this.camForward = camForward;
            this.frustum = frustum;
            this.screenHeight = screenHeight;
            this.fieldOfView = fieldOfView;
        }

        private uint TraverseAndBuildRenderingQueue() {
            //Camera Data
            Vector3 cameraPosition;
            Vector3 camForward;
            Plane[] frustum;
            float screenHeight;
            float fieldOfView;

            PriorityQueue<double, Node> toProcess = new HeapPriorityQueue<double, Node>();

            if (this.frustum == null) {
                return 0;
            }
            cameraPosition = this.cameraPosition;
            camForward = this.camForward;
            frustum = this.frustum;
            screenHeight = this.screenHeight;
            fieldOfView = this.fieldOfView;
            
            //Clearing Queues
            uint renderingpointcount = 0;
            uint maxnodestoload = nodesLoadedPerFrame;
            uint maxnodestorender = nodesGOsPerFrame;
            HashSet<Node> newVisibleNodes = new HashSet<Node>();

            foreach (Node rootNode in rootNodes) {
                Vector3 center = rootNode.BoundingBox.GetBoundsObject().center;
                double distance = (center - cameraPosition).magnitude;
                double slope = Math.Tan(fieldOfView / 2 * Mathf.Deg2Rad);
                double projectedSize = (screenHeight / 2.0) * rootNode.BoundingBox.Radius() / (slope * distance);
                if (projectedSize > minNodeSize) {
                    Vector3 camToNodeCenterDir = (center - cameraPosition).normalized;
                    double angle = Math.Acos(camForward.x * camToNodeCenterDir.x + camForward.y * camToNodeCenterDir.y + camForward.z * camToNodeCenterDir.z);
                    double angleWeight = Math.Abs(angle) + 1.0; //+1, to prevent divsion by zero
                    double priority = projectedSize / angleWeight;
                    toProcess.Enqueue(rootNode, priority);
                } else {
                    DeleteNode(rootNode);
                }
            }
            var nodeCount = 0;
           // Debug.Log("TOPROCESS: " + toProcess.Count);
            while (!toProcess.IsEmpty() && running) {
                Node n = toProcess.Dequeue(); //Min Node Size was already checked
                nodeCount++;
                //Is Node inside frustum?
                if (Util.InsideFrustum(n.BoundingBox, frustum)) {
                    
                    bool loadchildren = false;
 
                        if (n.PointCount == -1) {
                            if (maxnodestoload > 0) {
                                loadingThread.ScheduleForLoading(n);
                                --maxnodestoload;
                                loadchildren = true;
                            }
                        } else if (renderingpointcount + n.PointCount <= pointBudget) {
                            if (n.HasGameObjects()) {
                                renderingpointcount += (uint)n.PointCount;
                                visibleNodes.Remove(n);
                                newVisibleNodes.Add(n);
                                loadchildren = true;
                            } else if (n.HasPointsToRender()) {
                                //Might be in Cache -> Withdraw
                                if (maxnodestorender > 0) {
                                    cache.Withdraw(n);
                                    renderingpointcount += (uint)n.PointCount;
                                    toRender.Enqueue(n);
                                    --maxnodestorender;
                                    newVisibleNodes.Add(n);
                                    loadchildren = true;
                                }
                            } else {
                                if (maxnodestoload > 0) {
                                    loadingThread.ScheduleForLoading(n);
                                    --maxnodestoload;
                                    loadchildren = true;
                                }
                            }
                        } else {
                            maxnodestoload = 0;
                            maxnodestorender = 0;
                            if (n.HasGameObjects()) {
                                visibleNodes.Remove(n);
                                DeleteNode(n);
                            }
                        }


                    if (loadchildren) {
                        foreach (Node child in n) {
                            Vector3 center = child.BoundingBox.GetBoundsObject().center;
                            double distance = (center - cameraPosition).magnitude;
                            double slope = Math.Tan(fieldOfView / 2 * Mathf.Deg2Rad);
                            double projectedSize = (screenHeight / 2.0) * child.BoundingBox.Radius() / (slope * distance);
                            if (projectedSize > minNodeSize) {
                                Vector3 camToNodeCenterDir = (center - cameraPosition).normalized;
                                double angle = Math.Acos(camForward.x * camToNodeCenterDir.x + camForward.y * camToNodeCenterDir.y + camForward.z * camToNodeCenterDir.z);
                                double angleWeight = Math.Abs(angle) + 1.0; //+1, to prevent divsion by zero
                                double priority = projectedSize / angleWeight;
                                toProcess.Enqueue(child, priority);
                            } else {
                                DeleteNode(child);
                            }
                        }
                    }

                } else {
                    //This node or its children might be visible
                    DeleteNode(n);
                }
            }
            foreach (Node n in visibleNodes) {
                DeleteNode(n);
            }
            visibleNodes = newVisibleNodes;
            Wait = true;
           // Debug.Log("NODECOUNT: " + nodeCount);
            return renderingpointcount;
        }

        private void DeleteNode(Node currentNode) {
 
                if (!currentNode.HasGameObjects()) {
                    return;
                }

            Queue<Node> nodesToDelete = new Queue<Node>();
            nodesToDelete.Enqueue(currentNode);
            Stack<Node> tempToDelete = new Stack<Node>();   //To assure better order in cache

            while (nodesToDelete.Count != 0) {
                Node child = nodesToDelete.Dequeue();

                if (child.HasGameObjects()) {

                    tempToDelete.Push(child);

                    foreach (Node childchild in child) {
                        nodesToDelete.Enqueue(childchild);
                    }
                } 
            }
            while (tempToDelete.Count != 0) {
                Node n = tempToDelete.Pop();
                toDelete.Enqueue(n);
            }
        }

        public void Stop() {
            running = false;
        }

        public void StopAndWait() {
            running = false;
            if (thread != null) {
                thread.Join();
                thread = null;
            }

        }

    }
}
