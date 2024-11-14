using System;
using System.Collections;
using System.Collections.Generic;
using BAPointCloudRenderer.DataStructures;
using BAPointCloudRenderer.CloudData;
using BAPointCloudRenderer.CloudController;
using System.Threading;
using UnityEngine;

namespace BAPointCloudRenderer.Loading
{
    /// <summary>
    /// The Loading Thread of the V2-Rendering-System (see Bachelor Thesis chapter 3.2.6 "The Loading Thread").
    /// Responsible for loading the point data.
    /// </summary>
    class V2LoadingThread
    {
        private QueueCR<Node> loadingQueue;
        private bool running = true;
        private V2Cache cache;

        private PointCloudLoader controller;

        public V2LoadingThread(V2Cache cache)
        {
            loadingQueue = new QueueCR<Node>();
            this.cache = cache;
        }

        public void Start(MonoBehaviour mono, PointCloudLoader loader)
        {
            running = true;
            controller = loader;
            mono.StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            while (running)
            {
                if (loadingQueue.TryDequeue(out Node n))
                {
                    if (!n.HasPointsToRender() && !n.HasGameObjects())
                    {
                        var waitForLoad = controller.StartCoroutine(controller.LoadPointsForNode(n));
                        //CloudLoader.LoadPointsForNode(n);
                        yield return waitForLoad;
                        cache.Insert(n);
                    }
       
                }
                
                //Debug.Log(loadingQueue.Count);
                if (loadingQueue.Count == 0)
                    yield return new WaitForEndOfFrame();
            }
        }

        public void Stop()
        {
            running = false;
        }

        /// <summary>
        /// Schedules the given node for loading.
        /// </summary>
        /// <param name="node">not null</param>
        public void ScheduleForLoading(Node node)
        {
            loadingQueue.Enqueue(node);
        }
    }
}