using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Stagecraft
{
    /// <summary>
    /// Backwards-compatible fluent API that now builds typed Stagecraft action specs.
    /// </summary>
    public class ActionBuilder
    {
        readonly XrActionPlan m_Plan = new XrActionPlan();
        readonly ActionRunner m_Runner;
        bool m_Grabbed;
        GameObject m_CurrentTarget;
        GameObject m_CurrentController;

        public XrExecutionResult LastResult => m_Runner.LastResult;
        public XrActionPlan Plan => m_Plan;

        public ActionBuilder()
            : this(new ActionRunner())
        {
        }

        public ActionBuilder(ActionRunner runner)
        {
            m_Runner = runner ?? new ActionRunner();
        }

        public ActionBuilder Add(XrActionSpec action)
        {
            if (action != null)
                m_Plan.actions.Add(action);
            return this;
        }

        public ActionBuilder AddRange(IEnumerable<XrActionSpec> actions)
        {
            if (actions == null)
                return this;

            foreach (var action in actions)
                Add(action);
            return this;
        }

        public ActionBuilder Grab()
        {
            return SelectEnter();
        }

        public ActionBuilder GrabHold(float duration = 1.0f)
        {
            SelectEnter();
            Wait(duration);
            m_Grabbed = true;
            return this;
        }

        public ActionBuilder ReleaseAllKeys()
        {
            var selectExit = New(XrActionKind.SelectExit, m_CurrentTarget, m_CurrentController);
            selectExit.driverMode = XrDriverMode.XRDeviceSimulatorKeyboard;
            Add(selectExit);

            var deactivate = New(XrActionKind.Deactivate, m_CurrentTarget, m_CurrentController);
            deactivate.driverMode = XrDriverMode.XRDeviceSimulatorKeyboard;
            Add(deactivate);
            m_Grabbed = false;
            return this;
        }

        public ActionBuilder Trigger()
        {
            var action = New(XrActionKind.Activate, m_CurrentTarget, m_CurrentController);
            action.driverMode = XrDriverMode.XRDeviceSimulatorKeyboard;
            Add(action);
            return this;
        }

        public ActionBuilder TriggerHold(float duration = 1.0f)
        {
            Trigger();
            Wait(duration);
            return this;
        }

        public ActionBuilder MoveHold(Key movementKey, float duration = 1.0f)
        {
            var delta = KeyToMoveDelta(movementKey);
            var euler = KeyToRotateDelta(movementKey);
            if (delta != Vector3.zero)
            {
                var action = New(XrActionKind.MoveController, m_CurrentTarget, m_CurrentController);
                action.driverMode = XrDriverMode.XRDeviceSimulatorKeyboard;
                action.parameters["delta"] = VectorToString(delta);
                action.parameters["duration"] = FormatFloat(duration);
                action.parameters["keyFallback"] = movementKey.ToString();
                Add(action);
            }
            else if (euler != Vector3.zero)
            {
                var action = New(XrActionKind.RotateController, m_CurrentTarget, m_CurrentController);
                action.driverMode = XrDriverMode.XRDeviceSimulatorKeyboard;
                action.parameters["eulerDelta"] = VectorToString(euler);
                action.parameters["duration"] = FormatFloat(duration);
                action.parameters["keyFallback"] = movementKey.ToString();
                Add(action);
            }
            else
            {
                var action = New(XrActionKind.Wait, null, null);
                action.driverMode = XrDriverMode.XRDeviceSimulatorKeyboard;
                action.parameters["duration"] = FormatFloat(duration);
                action.parameters["keyFallback"] = movementKey.ToString();
                Add(action);
            }
            return this;
        }

        public ActionBuilder MoveUp(float duration = 1.0f) => MoveHold(Key.E, duration);
        public ActionBuilder MoveDown(float duration = 1.0f) => MoveHold(Key.Q, duration);
        public ActionBuilder MoveForward(float duration = 1.0f) => MoveHold(Key.W, duration);
        public ActionBuilder MoveBackward(float duration = 1.0f) => MoveHold(Key.S, duration);
        public ActionBuilder MoveLeft(float duration = 1.0f) => MoveHold(Key.A, duration);
        public ActionBuilder MoveRight(float duration = 1.0f) => MoveHold(Key.D, duration);
        public ActionBuilder RotateLeft(float duration = 0.1f) => MoveHold(Key.LeftArrow, duration);
        public ActionBuilder RotateRight(float duration = 0.1f) => MoveHold(Key.RightArrow, duration);
        public ActionBuilder RotateUp(float duration = 0.1f) => MoveHold(Key.UpArrow, duration);
        public ActionBuilder RotateDown(float duration = 0.1f) => MoveHold(Key.DownArrow, duration);

        public ActionBuilder NavigateTo(GameObject player, GameObject target, float moveSpeed = 1.0f, float interactionAngle = 5.0f, float interactionDistance = 1.0f)
        {
            m_CurrentController = player;
            m_CurrentTarget = target;
            var action = New(XrActionKind.NavigateTo, target, player);
            action.driverMode = XrDriverMode.DirectTransform;
            action.parameters["moveSpeed"] = FormatFloat(moveSpeed);
            action.parameters["interactionAngle"] = FormatFloat(interactionAngle);
            action.parameters["interactionDistance"] = FormatFloat(interactionDistance);
            Add(action);
            return this;
        }

        public ActionBuilder MoveControllerTo(GameObject controller, GameObject target)
        {
            m_CurrentController = controller;
            m_CurrentTarget = target;
            var action = New(XrActionKind.MoveControllerTo, target, controller);
            action.driverMode = XrDriverMode.XRDeviceSimulatorKeyboard;
            action.parameters["keyboardFallback"] = "true";
            Add(action);
            return this;
        }

        public ActionBuilder SelectEnter()
        {
            var action = New(XrActionKind.SelectEnter, m_CurrentTarget, m_CurrentController);
            action.driverMode = XrDriverMode.XRDeviceSimulatorKeyboard;
            Add(action);
            m_Grabbed = true;
            return this;
        }

        public ActionBuilder SelectExit()
        {
            var action = New(XrActionKind.SelectExit, m_CurrentTarget, m_CurrentController);
            action.driverMode = XrDriverMode.XRDeviceSimulatorKeyboard;
            Add(action);
            m_Grabbed = false;
            return this;
        }

        public ActionBuilder Wait(float duration)
        {
            var action = New(XrActionKind.Wait, null, null);
            action.parameters["duration"] = FormatFloat(duration);
            Add(action);
            return this;
        }

        public IEnumerator Execute()
        {
            yield return ExecutePlan(m_Plan);
        }

        public IEnumerator ExecutePlan(XrActionPlan plan)
        {
            yield return m_Runner.Execute(plan);
            if (m_Runner.LastResult != null && !m_Runner.LastResult.Succeeded)
                throw new Exception(m_Runner.LastResult.ToJson());
        }

        public IEnumerator ExecuteJson(string json)
        {
            yield return ExecutePlan(XrActionPlan.FromJson(json));
        }

        public ActionBuilder Clear()
        {
            m_Plan.actions.Clear();
            m_Grabbed = false;
            m_CurrentTarget = null;
            m_CurrentController = null;
            return this;
        }

        public int Count => m_Plan.actions.Count;

        XrActionSpec New(XrActionKind kind, GameObject target, GameObject controller)
        {
            var action = XrActionSpec.Create(kind);
            action.target = XrObjectRef.FromGameObject(target);
            action.controller = XrObjectRef.FromGameObject(controller);
            if (m_Grabbed)
                action.parameters["selectHeld"] = "true";
            return action;
        }

        static Vector3 KeyToMoveDelta(Key key)
        {
            switch (key)
            {
                case Key.W: return new Vector3(0, 0, 0.25f);
                case Key.S: return new Vector3(0, 0, -0.25f);
                case Key.A: return new Vector3(-0.25f, 0, 0);
                case Key.D: return new Vector3(0.25f, 0, 0);
                case Key.E: return new Vector3(0, 0.25f, 0);
                case Key.Q: return new Vector3(0, -0.25f, 0);
                default: return Vector3.zero;
            }
        }

        static Vector3 KeyToRotateDelta(Key key)
        {
            switch (key)
            {
                case Key.LeftArrow: return new Vector3(0, -15f, 0);
                case Key.RightArrow: return new Vector3(0, 15f, 0);
                case Key.UpArrow: return new Vector3(-15f, 0, 0);
                case Key.DownArrow: return new Vector3(15f, 0, 0);
                default: return Vector3.zero;
            }
        }

        static string FormatFloat(float value) => value.ToString(CultureInfo.InvariantCulture);

        static string VectorToString(Vector3 value)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0},{1},{2}", value.x, value.y, value.z);
        }
    }
}
