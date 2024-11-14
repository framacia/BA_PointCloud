// using System;
// using System.Collections;
// using BAPointCloudRenderer.DataStructures;
// using BAPointCloudRenderer.CloudData;
// using BAPointCloudRenderer.CloudController;
// using System.Threading;
// using UnityEngine;
//
// namespace BAPointCloudRenderer.Loading
// {
//     /// <summary>
//     /// The Loading Thread of the V2-Rendering-System (see Bachelor Thesis chapter 3.2.6 "The Loading Thread").
//     /// Responsible for loading the point data.
//     /// </summary>
//     class V2LoadingThreadCR
//     {
//         private QueueCR<Node> loadingQueue;
//
//         private PointCloudLoaderCR _controller;
//         private bool running = true;
//         private V2CacheCR cache;
//
//         public V2LoadingThreadCR(V2CacheCR cache)
//         {
//             loadingQueue = new QueueCR<Node>();
//             this.cache = cache;
//         }
//
//         public void Start(MonoBehaviour mono, PointCloudLoaderCR loader)
//         {
//             running = true;
//             _controller = loader;
//             mono.StartCoroutine(Run());
//         }
//
//         public IEnumerator Run()
//         {
//             while (running)
//             {
//                 if (loadingQueue.TryDequeue(out Node n))
//                 {
//                     if (!n.HasPointsToRender() && !n.HasGameObjects())
//                     {
//                         var waitForLoad = _controller.StartCoroutine(_controller.LoadPointsForNode(n));
//                         yield return waitForLoad;
//                         cache.Insert(n);
//                     }
//                 }
//
//                 //Debug.Log(loadingQueue.Count);
//                 if (loadingQueue.Count == 0)
//                     yield return new WaitForEndOfFrame();
//             }
//         }
//
//
//         public void Stop()
//         {
//             running = false;
//         }
//
//         /// <summary>
//         /// Schedules the given node for loading.
//         /// </summary>
//         /// <param name="node">not null</param>
//         public void ScheduleForLoading(Node node)
//         {
//             loadingQueue.Enqueue(node);
//         }
//     }
// }