using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.VFX;
using UnityEditor.Experimental.VFX;
using UnityEditor;
using UnityEngine.TestTools;
using System.Linq;
using UnityEditor.VFX.UI;
using System.Collections;
using System.Collections.Generic;
using UnityEditor.VFX.Block;

namespace UnityEditor.VFX.Test
{
    public class VFXSpaceBoundTest
    {
        string tempFilePath = "Assets/TmpTests/vfxTest.vfx";

        VFXGraph MakeTemporaryGraph()
        {
            if (System.IO.File.Exists(tempFilePath))
            {
                AssetDatabase.DeleteAsset(tempFilePath);
            }

            var asset = VisualEffectResource.CreateNewAsset(tempFilePath);

            VisualEffectResource resource = asset.GetResource(); // force resource creation

            VFXGraph graph = ScriptableObject.CreateInstance<VFXGraph>();

            graph.visualEffectResource = resource;

            return graph;
        }

        [TearDown]
        public void CleanUp()
        {
            AssetDatabase.DeleteAsset(tempFilePath);
        }

        private static VFXCoordinateSpace[] available_Space = { VFXCoordinateSpace.Local, VFXCoordinateSpace.World };

        [UnityTest]
        [Timeout(1000 * 10)]
        public IEnumerator CreateAssetAndComponent_Space_Bounds([ValueSource("available_Space")] VFXCoordinateSpace systemSpace, [ValueSource("available_Space")] VFXCoordinateSpace boundSpace)
        {
            var objectPosition = new Vector3(0.123f, 0.0f, 0.0f);
            var boundPosition = new Vector3(0.0f, 0.0987f, 0.0f);

            EditorApplication.ExecuteMenuItem("Window/General/Game");

            var graph = MakeTemporaryGraph();

            var spawnerContext = ScriptableObject.CreateInstance<VFXBasicSpawner>();
            var blockConstantRate = ScriptableObject.CreateInstance<VFXSpawnerConstantRate>();

            var basicInitialize = ScriptableObject.CreateInstance<VFXBasicInitialize>();
            var basicUpdate = ScriptableObject.CreateInstance<VFXBasicUpdate>();
            var quadOutput = ScriptableObject.CreateInstance<VFXQuadOutput>();
            quadOutput.SetSettingValue("blendMode", VFXAbstractParticleOutput.BlendMode.Additive);

            var setLifetime = ScriptableObject.CreateInstance<SetAttribute>(); //only needed to allocate a minimal attributeBuffer
            setLifetime.SetSettingValue("attribute", "lifetime");
            setLifetime.inputSlots[0].value = 1.0f;
            basicInitialize.AddChild(setLifetime);

            spawnerContext.AddChild(blockConstantRate);
            graph.AddChild(spawnerContext);
            graph.AddChild(basicInitialize);
            graph.AddChild(basicUpdate);
            graph.AddChild(quadOutput);
            basicInitialize.LinkFrom(spawnerContext);
            basicUpdate.LinkFrom(basicInitialize);
            quadOutput.LinkFrom(basicUpdate);

            basicInitialize.space = systemSpace;
            basicInitialize.inputSlots[0].space = boundSpace;
            basicInitialize.inputSlots[0][0].value = boundPosition;
            basicInitialize.inputSlots[0][1].value = Vector3.one * 5.0f;

            graph.RecompileIfNeeded();

            var gameObj = new GameObject("CreateAssetAndComponentToCheckBound");
            gameObj.transform.position = objectPosition;
            var vfxComponent = gameObj.AddComponent<VisualEffect>();
            vfxComponent.visualEffectAsset = graph.visualEffectResource.asset;

            var cameraObj = new GameObject("CreateAssetAndComponentToCheckBound_Camera");
            var camera = cameraObj.AddComponent<Camera>();
            camera.transform.localPosition = Vector3.one;
            camera.transform.LookAt(vfxComponent.transform);

            int maxFrame = 512;
            while (vfxComponent.culled && --maxFrame > 0)
            {
                yield return null;
            }
            Assert.IsTrue(maxFrame > 0);
            yield return null; //wait for exactly one more update if visible

            var renderer = vfxComponent.GetComponent<VFXRenderer>();
            var parentFromCenter = VFXSpacePropagationTest.CollectParentExpression(basicInitialize.inputSlots[0][0].GetExpression()).ToArray();
            if (systemSpace == VFXCoordinateSpace.Local && boundSpace == VFXCoordinateSpace.Local)
            {
                Assert.IsFalse(parentFromCenter.Any(o => o.operation == VFXExpressionOperation.LocalToWorld || o.operation == VFXExpressionOperation.WorldToLocal));
                Assert.AreEqual(boundPosition.x + objectPosition.x, renderer.bounds.center.x, 0.0001);
                Assert.AreEqual(boundPosition.y + objectPosition.y, renderer.bounds.center.y, 0.0001);
                Assert.AreEqual(boundPosition.z + objectPosition.z, renderer.bounds.center.z, 0.0001);
            }
            else if (systemSpace == VFXCoordinateSpace.World && boundSpace == VFXCoordinateSpace.Local)
            {
                Assert.IsFalse(parentFromCenter.Any(o => o.operation == VFXExpressionOperation.LocalToWorld || o.operation == VFXExpressionOperation.WorldToLocal));
                Assert.AreEqual(boundPosition.x + objectPosition.x, renderer.bounds.center.x, 0.0001);
                Assert.AreEqual(boundPosition.y + objectPosition.y, renderer.bounds.center.y, 0.0001);
                Assert.AreEqual(boundPosition.z + objectPosition.z, renderer.bounds.center.z, 0.0001);
            }
            else if (systemSpace == VFXCoordinateSpace.World && boundSpace == VFXCoordinateSpace.World)
            {
                Assert.IsTrue(parentFromCenter.Count(o => o.operation == VFXExpressionOperation.WorldToLocal) == 1);
                //object position has no influence in that case
                Assert.AreEqual(boundPosition.x, renderer.bounds.center.x, 0.0001);
                Assert.AreEqual(boundPosition.y, renderer.bounds.center.y, 0.0001);
                Assert.AreEqual(boundPosition.z, renderer.bounds.center.z, 0.0001);
            }
            else if (systemSpace == VFXCoordinateSpace.Local && boundSpace == VFXCoordinateSpace.World)
            {
                Assert.IsTrue(parentFromCenter.Count(o => o.operation == VFXExpressionOperation.WorldToLocal) == 1);
                //object position has no influence in that case
                Assert.AreEqual(boundPosition.x, renderer.bounds.center.x, 0.0001);
                Assert.AreEqual(boundPosition.y, renderer.bounds.center.y, 0.0001);
                Assert.AreEqual(boundPosition.z, renderer.bounds.center.z, 0.0001);
            }
            else
            {
                //Unknown case, should not happen
                Assert.IsFalse(true);
            }

            UnityEngine.Object.DestroyImmediate(vfxComponent);
            UnityEngine.Object.DestroyImmediate(cameraObj);
        }
    }
}
