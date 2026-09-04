using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Llm2Pcg.Tests.EditMode
{
    public sealed class PCGPublicDemoSceneTests
    {
        [Test]
        public void PrimitiveDemoScene_HasSingleBootstrap()
        {
            const string scenePath = "Assets/PCGPublicDemo/Scenes/PrimitiveDemo.unity";
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                MonoBehaviour[] bootstraps = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true))
                    .Where(component => component != null && component.GetType().FullName == "Llm2Pcg.Demo.PCGPublicDemoBootstrap")
                    .ToArray();

                Assert.That(bootstraps, Has.Length.EqualTo(1));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
