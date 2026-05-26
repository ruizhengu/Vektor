using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Vektor
{
    public class VektorRuntimeTests
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

        [Test]
        public void ResolverPrefersStableVektorId()
        {
            var first = new GameObject("Duplicate");
            var second = new GameObject("Duplicate");
            try
            {
                var firstId = first.AddComponent<VektorId>();
                firstId.EnsureId();
                var secondId = second.AddComponent<VektorId>();
                secondId.EnsureId();

                var resolved = new ObjectResolver().Resolve(new XrObjectRef
                {
                    vektorId = secondId.Id,
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
    }
}
