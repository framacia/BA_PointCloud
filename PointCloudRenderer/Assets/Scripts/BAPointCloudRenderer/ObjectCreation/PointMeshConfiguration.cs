﻿using BAPointCloudRenderer.CloudData;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace BAPointCloudRenderer.ObjectCreation
{
    /// <summary>
    /// Renders every point as a single pixel using the points primitive. As described in the Bachelor Thesis in chapter 3.3.1 "Single-Pixel Point Rendering".
    /// </summary>
    class PointMeshConfiguration : MeshConfiguration
    {
        /// <summary>
        /// If set to true, the Bounding Boxes of the individual octree nodes will be displayed.
        /// </summary>
        public bool displayLOD = false;

        public bool usesParticles = false;
        public ParticleSystem particles;
        public bool forceToMultipleOfThree;

        private Material material;
        private HashSet<GameObject> gameObjectCollection = null;

        public void Start()
        {
            material = new Material(Shader.Find("Custom/PointShader"));
            material.enableInstancing = true;
            gameObjectCollection = new HashSet<GameObject>();
        }

        public void Update()
        {
            if (displayLOD)
            {
                foreach (GameObject go in gameObjectCollection)
                {
                    Utility.BBDraw.DrawBoundingBox(go.GetComponent<BoundingBoxComponent>().boundingBox, null, Color.red,
                        false);
                }
            }
        }

        public override GameObject CreateGameObject(string name, Vector3[] vertexData, Color[] colorData,
            BoundingBox boundingBox, Transform parent, string version, Vector3d translationV2)
        {
            GameObject gameObject = new GameObject(name);

            if (forceToMultipleOfThree)
            {
                // Convert the array to a List for dynamic removal
                List<Vector3> vertexList = new List<Vector3>(vertexData);
                List<Color> colorList = new List<Color>(colorData);

                // Remove elements from the end until the count is a multiple of 3
                while (vertexList.Count % 3 != 0)
                {
                    vertexList.RemoveAt(vertexList.Count - 1);
                    colorList.RemoveAt(colorList.Count - 1);
                }

                // Convert back to an array if needed
                vertexData = vertexList.ToArray();
                colorData = colorList.ToArray();
            }

            Mesh mesh = new Mesh();

            MeshFilter filter = gameObject.AddComponent<MeshFilter>();
            filter.mesh = mesh;
            MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.material = material;

            int[] indices = new int[vertexData.Length];
            for (int i = 0; i < vertexData.Length; ++i)
            {
                indices[i] = i;
            }

            mesh.vertices = vertexData;
            mesh.colors = colorData;

            if (usesParticles)
            {
                mesh.SetIndices(indices, MeshTopology.Triangles, 0);
                //Particle System
                var particleSystem = Instantiate(particles, gameObject.transform);
                var shape = particleSystem.shape;
                shape.meshRenderer = renderer;

                renderer.enabled = false;
            }
            else
                mesh.SetIndices(indices, MeshTopology.Points, 0);

            //Set Translation
            if (version == "2.0")
            {
                // 20230125: potree v2 vertices have absolute coordinates,
                // hence all gameobjects need to reside at Vector.Zero.
                // And: the position must be set after parenthood has been granted.
                //gameObject.transform.Translate(boundingBox.Min().ToFloatVector());
                gameObject.transform.SetParent(parent, false);
                gameObject.transform.localPosition = translationV2.ToFloatVector();
            }
            else
            {
                gameObject.transform.Translate(boundingBox.Min().ToFloatVector());
                gameObject.transform.SetParent(parent, false);
            }

            gameObject.AddComponent<BoundingBoxComponent>().boundingBox = boundingBox;


            if (gameObjectCollection != null)
            {
                gameObjectCollection.Add(gameObject);
            }

            return gameObject;
        }

        public override int GetMaximumPointsPerMesh()
        {
            return 65000;
        }

        public override void RemoveGameObject(GameObject gameObject)
        {
            if (gameObjectCollection != null)
            {
                gameObjectCollection.Remove(gameObject);
            }

            if (gameObject != null)
            {
                Destroy(gameObject.GetComponent<MeshFilter>().sharedMesh);
                Destroy(gameObject);
            }
        }
    }
}