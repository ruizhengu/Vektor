using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Stagecraft
{
    public class StagecraftRuntimeTests
    {
        [UnityTest]
        public IEnumerator WaitPlanFromJsonCompletes()
        {
            var json = @"{
              ""schemaVersion"": 1,
              ""planId"": ""wait-smoke"",
              ""actions"": [
                { ""id"": ""wait-1"", ""type"": ""Wait"", ""timeoutSeconds"": 1, ""parameters"": { ""duration"": ""0.01"" } }
              ]
            }";

            var runner = new ActionRunner();
            yield return runner.Execute(XrActionPlan.FromJson(json));

            Assert.IsTrue(runner.LastResult.Succeeded);
            Assert.AreEqual("wait-smoke", runner.LastResult.planId);
            Assert.AreEqual(XrActionStatus.Passed, runner.LastResult.actions[0].status);
        }

        [Test]
        public void RejectsUnsupportedSchemaVersion()
        {
            var json = @"{ ""schemaVersion"": 99, ""planId"": ""bad"", ""actions"": [] }";
            Assert.Throws<System.NotSupportedException>(() => XrActionPlan.FromJson(json));
        }

        [Test]
        public void JsonPlanParsesExplicitDriverMode()
        {
            var json = @"{
              ""schemaVersion"": 1,
              ""planId"": ""driver-mode"",
              ""actions"": [
                { ""id"": ""move-1"", ""type"": ""MoveController"", ""driverMode"": ""DirectTransform"", ""parameters"": { ""delta"": ""1,0,0"" } }
              ]
            }";

            var plan = XrActionPlan.FromJson(json);

            Assert.AreEqual(XrDriverMode.DirectTransform, plan.actions[0].driverMode);
            StringAssert.Contains(@"""driverMode"": ""DirectTransform""", plan.ToJson());
            StringAssert.Contains(@"""type"": ""MoveController""", plan.ToJson());
        }

        [Test]
        public void FluentBuilderKeepsLegacyKeyboardDriverMode()
        {
            var controller = new GameObject("Controller");
            var target = new GameObject("Target");
            try
            {
                var builder = new ActionBuilder()
                    .MoveControllerTo(controller, target)
                    .GrabHold(0.01f)
                    .Trigger();

                Assert.AreEqual(XrDriverMode.XRDeviceSimulatorKeyboard, builder.Plan.actions[0].driverMode);
                Assert.AreEqual(XrDriverMode.XRDeviceSimulatorKeyboard, builder.Plan.actions[1].driverMode);
                Assert.AreEqual(XrDriverMode.XRDeviceSimulatorKeyboard, builder.Plan.actions[3].driverMode);
            }
            finally
            {
                Object.DestroyImmediate(controller);
                Object.DestroyImmediate(target);
            }
        }

        [TestCase("stagecraftId")]
        [TestCase("vektorId")]
        public void ResolverPrefersStableIdOverName(string idField)
        {
            var first = new GameObject("Duplicate");
            var second = new GameObject("Duplicate");
            try
            {
                var firstId = first.AddComponent<StagecraftId>();
                firstId.EnsureId();
                var secondId = second.AddComponent<StagecraftId>();
                secondId.EnsureId();

                var resolved = new ObjectResolver().Resolve(new XrObjectRef
                {
                    stagecraftId = idField == "stagecraftId" ? secondId.Id : null,
                    vektorId = idField == "vektorId" ? secondId.Id : null,
                    name = "Duplicate"
                });

                Assert.AreSame(second, resolved);
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        [UnityTest]
        public IEnumerator MoveControllerActionMovesTransform()
        {
            var controller = new GameObject("Runtime Controller");
            try
            {
                var plan = new XrActionPlan { planId = "move-controller" };
                var action = XrActionSpec.Create(XrActionKind.MoveController, "move-1");
                action.controller = XrObjectRef.FromGameObject(controller);
                action.parameters["delta"] = "1,0,0";
                action.parameters["duration"] = "0.01";
                plan.actions.Add(action);

                var runner = new ActionRunner();
                yield return runner.Execute(plan);

                Assert.IsTrue(runner.LastResult.Succeeded);
                Assert.Greater(controller.transform.position.x, 0.9f);
            }
            finally
            {
                Object.DestroyImmediate(controller);
            }
        }

        [UnityTest]
        public IEnumerator MissingRequiredTargetProducesStructuredFailure()
        {
            var plan = new XrActionPlan { planId = "missing-target" };
            plan.actions.Add(XrActionSpec.Create(XrActionKind.AssertEventFired, "assert-1"));

            var runner = new ActionRunner();
            yield return runner.Execute(plan);

            Assert.AreEqual(XrActionStatus.Failed, runner.LastResult.status);
            Assert.AreEqual("assert-1", runner.LastResult.actions[0].actionId);
            Assert.IsNotEmpty(runner.LastResult.actions[0].message);
        }

        [Test]
        public void AssertEventFiredPrefersStagecraftIdOverName()
        {
            var first = new GameObject("Duplicate");
            var second = new GameObject("Duplicate");
            try
            {
                var firstId = first.AddComponent<StagecraftId>();
                firstId.EnsureId();
                var secondId = second.AddComponent<StagecraftId>();
                secondId.EnsureId();

                var observations = new[]
                {
                    new XrObservation { eventType = "selectEntered", objectName = "Duplicate", objectId = firstId.Id }
                };

                var evaluator = new AssertionEvaluator();
                Assert.IsTrue(evaluator.AssertEventFired(observations, "selectEntered", first, out _));
                Assert.IsFalse(evaluator.AssertEventFired(observations, "selectEntered", second, out _));
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        [UnityTest]
        public IEnumerator ActionRunnerAcceptsCustomDriver()
        {
            var driver = new StubDriver();
            var controller = new GameObject("Ctrl");
            var target = new GameObject("Tgt");
            try
            {
                var plan = new XrActionPlan { planId = "custom-driver" };
                var action = XrActionSpec.Create(XrActionKind.SelectEnter, "sel-1");
                action.driverMode = XrDriverMode.XRInteractionManager;
                action.controller = XrObjectRef.FromGameObject(controller);
                action.target = XrObjectRef.FromGameObject(target);
                plan.actions.Add(action);

                using (var runner = new ActionRunner(driver: driver))
                {
                    yield return runner.Execute(plan);
                    Assert.IsTrue(runner.LastResult.Succeeded);
                    Assert.AreEqual(1, driver.SelectEnterCalls);
                }
            }
            finally
            {
                Object.DestroyImmediate(controller);
                Object.DestroyImmediate(target);
            }
        }

        sealed class StubDriver : IXrActionDriver
        {
            public int SelectEnterCalls { get; private set; }
            public IEnumerator NavigateTo(Transform player, Transform target, float moveSpeed, float interactionAngle, float interactionDistance, float timeoutSeconds, System.Func<string, bool> fail) { yield break; }
            public IEnumerator MoveControllerTo(Transform controller, Transform target, float speed, float threshold, float timeoutSeconds, System.Func<string, bool> fail) { yield break; }
            public IEnumerator MoveController(Transform controller, Vector3 delta, float duration) { yield break; }
            public IEnumerator RotateController(Transform controller, Vector3 eulerDelta, float duration) { yield break; }
            public bool SelectEnter(GameObject controller, GameObject target, out string error) { SelectEnterCalls++; error = null; return true; }
            public bool SelectExit(GameObject controller, GameObject target, out string error) { error = null; return true; }
            public bool Activate(GameObject controller, GameObject target, out string error) { error = null; return true; }
            public bool Deactivate(GameObject controller, GameObject target, out string error) { error = null; return true; }
        }
    }
}
