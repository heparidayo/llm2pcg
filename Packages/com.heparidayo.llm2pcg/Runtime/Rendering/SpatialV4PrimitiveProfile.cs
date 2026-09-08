using System;
using System.Collections.Generic;
using Llm2Pcg.Core.V4;
using UnityEngine;
using UnityEngine.Rendering;

namespace Llm2Pcg.Rendering
{
    /// <summary>Explicit semantic glyphs, not substitutes selected by asset-name substring.</summary>
    public static class SpatialV4PrimitiveProfile
    {
        public const string Version = "primitive-forest@1";
        public static bool Supports(string category, string type) => Array.IndexOf(SemanticCatalog.Types(category), type) >= 0;

        public static Color ColorFor(string type)
        {
            switch (type)
            {
                case "cherry_blossom": return new Color(.95f, .47f, .66f);
                case "conifer": return new Color(.10f, .32f, .23f);
                case "willow": return new Color(.44f, .62f, .18f);
                case "dead_tree": return new Color(.34f, .25f, .17f);
                case "rock": return new Color(.43f, .47f, .49f);
                case "flower": return new Color(.98f, .74f, .24f);
                case "mushroom": return new Color(.78f, .29f, .22f);
                case "water_lily": return new Color(.98f, .72f, .88f);
                case "reeds": return new Color(.58f, .59f, .22f);
                default: return new Color(.25f, .51f, .20f);
            }
        }

        public static Mesh Create(string category, string type)
        {
            if (!Supports(category, type)) throw new ArgumentException("Unsupported semantic primitive: " + category + "/" + type);
            var vertices = new List<Vector3>(); var colors = new List<Color>(); var triangles = new List<int>();
            Color crown = ColorFor(type), wood = new Color(.31f, .21f, .13f);
            void Box(Vector3 center, Vector3 size, Color color)
            {
                Vector3[] p = { new Vector3(-1,-1,-1),new Vector3(1,-1,-1),new Vector3(1,1,-1),new Vector3(-1,1,-1),
                    new Vector3(-1,-1,1),new Vector3(1,-1,1),new Vector3(1,1,1),new Vector3(-1,1,1) };
                int[] faces = { 0,3,2,1, 5,6,7,4, 4,7,3,0, 1,2,6,5, 3,7,6,2, 4,0,1,5 };
                for (int f = 0; f < 6; f++)
                {
                    int b = vertices.Count;
                    for (int k = 0; k < 4; k++) { vertices.Add(center + Vector3.Scale(p[faces[f*4+k]], size * .5f)); colors.Add(color); }
                    triangles.Add(b); triangles.Add(b+1); triangles.Add(b+2); triangles.Add(b); triangles.Add(b+2); triangles.Add(b+3);
                }
            }
            if (category == "trees")
            {
                Box(new Vector3(0, 1.1f, 0), new Vector3(.24f, 2.2f, .24f), wood);
                if (type == "dead_tree")
                { Box(new Vector3(.3f, 1.6f, 0), new Vector3(.8f, .16f, .15f), wood); Box(new Vector3(0, 1.95f, -.25f), new Vector3(.15f,.15f,.7f), wood); }
                else if (type == "conifer")
                {
                    for (int k = 0; k < 3; k++) Box(new Vector3(0, 1.45f + k * .6f, 0), new Vector3(1.3f-k*.35f,.65f,1.3f-k*.35f), crown);
                }
                else
                {
                    Box(new Vector3(0, 2.15f, 0), new Vector3(1.45f, .95f, 1.45f), crown);
                    Box(new Vector3(0, 2.75f, 0), new Vector3(.9f, .4f, .9f), crown * .93f);
                    if (type == "willow")
                        for (int k = 0; k < 4; k++)
                            Box(new Vector3(k < 2 ? (k == 0 ? -.62f : .62f) : 0, 1.25f, k >= 2 ? (k == 2 ? -.62f : .62f) : 0), new Vector3(.22f, 1.6f, .22f), crown);
                }
            }
            else if (type == "rock") Box(new Vector3(0,.35f,0), new Vector3(.9f,.7f,.7f), crown);
            else if (type == "bush") Box(new Vector3(0,.3f,0), new Vector3(.8f,.6f,.7f), crown);
            else if (type == "lily_pad" || type == "water_lily")
            {
                Box(new Vector3(0,.025f,0), new Vector3(.6f,.05f,.6f), new Color(.22f,.48f,.20f));
                if (type == "water_lily") Box(new Vector3(0,.12f,0), new Vector3(.22f,.15f,.22f), crown);
            }
            else
            {
                float tall = type == "reeds" ? .85f : .3f;
                Box(new Vector3(0,tall*.5f,0), new Vector3(.09f,tall,.09f), new Color(.25f,.42f,.16f));
                Box(new Vector3(0,tall,0), new Vector3(type == "mushroom" ? .35f : .2f,.12f,.2f), crown);
            }
            var mesh = new Mesh { name = Version + "/" + type, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
    }
}
