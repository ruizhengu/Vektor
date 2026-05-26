using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Vektor
{
    public enum XrActionKind
    {
        NavigateTo,
        MoveControllerTo,
        SelectEnter,
        SelectExit,
        Activate,
        Deactivate,
        MoveController,
        RotateController,
        Wait,
        AssertPropertyChanged,
        AssertEventFired,
        AssertTransformChanged
    }

    public enum XrDriverMode
    {
        XRDeviceSimulatorKeyboard,
        DirectTransform,
        XRInteractionManager
    }

    [Serializable]
    public class XrObjectRef
    {
        public string vektorId;
        public string intenXionId;
        public string scenePath;
        public string name;

        public static XrObjectRef FromGameObject(GameObject obj)
        {
            if (obj == null)
                return null;

            var id = obj.GetComponent<VektorId>();
            return new XrObjectRef
            {
                vektorId = id != null ? id.Id : null,
                scenePath = ObjectResolver.GetScenePath(obj),
                name = obj.name
            };
        }
    }

    [Serializable]
    public class XrActionSpec
    {
        public string id;
        [JsonConverter(typeof(StringEnumConverter))]
        public XrActionKind type;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(StringEnumConverter))]
        public XrDriverMode? driverMode;
        public XrObjectRef target;
        public XrObjectRef controller;
        public float timeoutSeconds = 5.0f;
        public Dictionary<string, string> parameters = new Dictionary<string, string>();

        public static XrActionSpec Create(XrActionKind kind, string id = null)
        {
            return new XrActionSpec
            {
                id = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString("N") : id,
                type = kind
            };
        }
    }

    [Serializable]
    public class XrActionPlan
    {
        public int schemaVersion = 1;
        public string planId = Guid.NewGuid().ToString("N");
        public List<XrActionSpec> actions = new List<XrActionSpec>();

        public static XrActionPlan FromJson(string json)
        {
            var plan = JsonConvert.DeserializeObject<XrActionPlan>(json);
            if (plan == null)
                throw new ArgumentException("Action plan JSON did not contain a valid plan.");
            if (plan.schemaVersion != 1)
                throw new NotSupportedException($"Unsupported Vektor action schema version {plan.schemaVersion}.");
            if (plan.actions == null)
                plan.actions = new List<XrActionSpec>();
            return plan;
        }

        public string ToJson()
        {
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }
    }

    public enum XrActionStatus
    {
        Pending,
        Passed,
        Failed,
        TimedOut,
        Skipped
    }

    [Serializable]
    public class XrObservation
    {
        public string eventType;
        public string objectName;
        public string objectId;
        public float time;
        public Vector3 position;
        public Quaternion rotation;
        public string detail;
    }

    [Serializable]
    public class XrActionResult
    {
        public string actionId;
        [JsonConverter(typeof(StringEnumConverter))]
        public XrActionKind type;
        [JsonConverter(typeof(StringEnumConverter))]
        public XrActionStatus status = XrActionStatus.Pending;
        public float startedAt;
        public float endedAt;
        public float elapsedSeconds;
        public string targetName;
        public string controllerName;
        public string driverMode;
        public string failureCode;
        public string message;
        public List<string> warnings = new List<string>();
        public List<XrObservation> observations = new List<XrObservation>();
    }

    [Serializable]
    public class XrExecutionResult
    {
        public string planId;
        public int schemaVersion;
        [JsonConverter(typeof(StringEnumConverter))]
        public XrActionStatus status = XrActionStatus.Pending;
        public List<XrActionResult> actions = new List<XrActionResult>();
        public List<string> warnings = new List<string>();

        public bool Succeeded => status == XrActionStatus.Passed;

        public string ToJson()
        {
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }
    }

    public class VektorId : MonoBehaviour
    {
        [SerializeField] string m_Id;

        public string Id => m_Id;

        public void EnsureId()
        {
            if (string.IsNullOrEmpty(m_Id))
                m_Id = Guid.NewGuid().ToString("N");
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            EnsureId();
        }
#endif
    }

    public class ObjectResolver
    {
        public GameObject Resolve(XrObjectRef reference, List<string> warnings = null)
        {
            if (reference == null)
                return null;

            var stableId = !string.IsNullOrEmpty(reference.vektorId) ? reference.vektorId : reference.intenXionId;
            if (!string.IsNullOrEmpty(stableId))
            {
                var byId = FindById(stableId);
                if (byId != null)
                    return byId;
                warnings?.Add($"Object id '{stableId}' was not found; falling back to scenePath/name.");
            }

            if (!string.IsNullOrEmpty(reference.scenePath))
            {
                var byPath = FindByScenePath(reference.scenePath);
                if (byPath != null)
                    return byPath;
                warnings?.Add($"Scene path '{reference.scenePath}' was not found; falling back to name.");
            }

            if (!string.IsNullOrEmpty(reference.name))
            {
                var matches = FindAllByName(reference.name);
                if (matches.Count == 1)
                    return matches[0];
                if (matches.Count > 1)
                    warnings?.Add($"Name '{reference.name}' matched {matches.Count} objects; using the first match. Add VektorId for stability.");
                if (matches.Count > 0)
                    return matches[0];
            }

            return null;
        }

        public static string GetScenePath(GameObject obj)
        {
            if (obj == null)
                return null;

            var names = new Stack<string>();
            var current = obj.transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }
            return string.Join("/", names.ToArray());
        }

        static GameObject FindById(string id)
        {
            foreach (var marker in UnityEngine.Object.FindObjectsOfType<VektorId>(true))
            {
                if (marker != null && marker.Id == id)
                    return marker.gameObject;
            }
            return null;
        }

        static GameObject FindByScenePath(string scenePath)
        {
            for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                var scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.isLoaded)
                    continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    var found = FindInPath(root.transform, scenePath.Split('/'), 0);
                    if (found != null)
                        return found.gameObject;
                }
            }
            return null;
        }

        static Transform FindInPath(Transform current, string[] path, int index)
        {
            if (current == null || path == null || index >= path.Length || current.name != path[index])
                return null;
            if (index == path.Length - 1)
                return current;

            foreach (Transform child in current)
            {
                var found = FindInPath(child, path, index + 1);
                if (found != null)
                    return found;
            }
            return null;
        }

        static List<GameObject> FindAllByName(string name)
        {
            var results = new List<GameObject>();
            for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                var scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.isLoaded)
                    continue;

                foreach (var root in scene.GetRootGameObjects())
                    CollectByName(root.transform, name, results);
            }
            return results;
        }

        static void CollectByName(Transform current, string name, List<GameObject> results)
        {
            if (current.name == name)
                results.Add(current.gameObject);

            foreach (Transform child in current)
                CollectByName(child, name, results);
        }
    }

    public class ObservationRecorder
    {
        readonly List<XrObservation> m_Observations = new List<XrObservation>();
        readonly Dictionary<GameObject, XRBaseInteractable> m_Registered = new Dictionary<GameObject, XRBaseInteractable>();

        public IReadOnlyList<XrObservation> Observations => m_Observations;

        public void Register(GameObject obj)
        {
            if (obj == null || m_Registered.ContainsKey(obj))
                return;

            var interactable = obj.GetComponent<XRBaseInteractable>();
            if (interactable == null)
                return;

            interactable.selectEntered.AddListener(OnSelectEntered);
            interactable.selectExited.AddListener(OnSelectExited);
            interactable.activated.AddListener(OnActivated);
            interactable.deactivated.AddListener(OnDeactivated);
            m_Registered[obj] = interactable;
        }

        public void RecordTransform(string eventType, GameObject obj, string detail = null)
        {
            if (obj == null)
                return;

            m_Observations.Add(CreateObservation(eventType, obj, detail));
        }

        public List<XrObservation> SnapshotSince(int index)
        {
            var snapshot = new List<XrObservation>();
            for (var i = Math.Max(0, index); i < m_Observations.Count; i++)
                snapshot.Add(m_Observations[i]);
            return snapshot;
        }

        public int Count => m_Observations.Count;

        XrObservation CreateObservation(string eventType, GameObject obj, string detail = null)
        {
            var id = obj.GetComponent<VektorId>();
            return new XrObservation
            {
                eventType = eventType,
                objectName = obj.name,
                objectId = id != null ? id.Id : null,
                time = Time.time,
                position = obj.transform.position,
                rotation = obj.transform.rotation,
                detail = detail
            };
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            m_Observations.Add(CreateObservation("selectEntered", args.interactableObject.transform.gameObject));
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            m_Observations.Add(CreateObservation("selectExited", args.interactableObject.transform.gameObject));
        }

        void OnActivated(ActivateEventArgs args)
        {
            m_Observations.Add(CreateObservation("activated", args.interactableObject.transform.gameObject));
        }

        void OnDeactivated(DeactivateEventArgs args)
        {
            m_Observations.Add(CreateObservation("deactivated", args.interactableObject.transform.gameObject));
        }
    }

    public class InteractorDriver
    {
        public IEnumerator NavigateTo(Transform player, Transform target, float moveSpeed, float interactionAngle, float interactionDistance, float timeoutSeconds, Func<string, bool> fail)
        {
            if (player == null || target == null)
            {
                fail("NavigateTo requires player and target transforms.");
                yield break;
            }

            var started = Time.time;
            while (Time.time - started <= timeoutSeconds)
            {
                var currentPos = player.position;
                var targetPos = target.position;
                var targetDirection = targetPos - currentPos;
                targetDirection.y = 0;

                if (targetDirection.sqrMagnitude > 0.0001f)
                {
                    var angle = Vector3.Angle(player.forward, targetDirection.normalized);
                    if (angle > interactionAngle)
                    {
                        var targetRotation = Quaternion.LookRotation(targetDirection.normalized);
                        player.rotation = Quaternion.RotateTowards(player.rotation, targetRotation, moveSpeed * 180f * Time.deltaTime);
                        yield return null;
                        continue;
                    }
                }

                var flatCurrentPos = new Vector3(currentPos.x, 0, currentPos.z);
                var flatTargetPos = new Vector3(targetPos.x, 0, targetPos.z);
                if (Vector3.Distance(flatCurrentPos, flatTargetPos) <= interactionDistance)
                    yield break;

                player.position = Vector3.MoveTowards(currentPos, new Vector3(targetPos.x, currentPos.y, targetPos.z), moveSpeed * Time.deltaTime);
                yield return null;
            }

            fail($"NavigateTo timed out after {timeoutSeconds:0.###}s.");
        }

        public IEnumerator MoveControllerTo(Transform controller, Transform target, float speed, float threshold, float timeoutSeconds, Func<string, bool> fail)
        {
            if (controller == null || target == null)
            {
                fail("MoveControllerTo requires controller and target transforms.");
                yield break;
            }

            var started = Time.time;
            while (Time.time - started <= timeoutSeconds)
            {
                if (Vector3.Distance(controller.position, target.position) <= threshold)
                    yield break;

                controller.position = Vector3.MoveTowards(controller.position, target.position, speed * Time.deltaTime);
                yield return null;
            }

            fail($"MoveControllerTo timed out after {timeoutSeconds:0.###}s.");
        }

        public IEnumerator MoveController(Transform controller, Vector3 delta, float duration)
        {
            if (controller == null)
                yield break;

            var started = Time.time;
            var origin = controller.position;
            duration = Mathf.Max(0.0001f, duration);
            while (Time.time - started < duration)
            {
                var t = Mathf.Clamp01((Time.time - started) / duration);
                controller.position = origin + delta * t;
                yield return null;
            }
            controller.position = origin + delta;
        }

        public IEnumerator RotateController(Transform controller, Vector3 eulerDelta, float duration)
        {
            if (controller == null)
                yield break;

            var started = Time.time;
            var origin = controller.rotation;
            var target = origin * Quaternion.Euler(eulerDelta);
            duration = Mathf.Max(0.0001f, duration);
            while (Time.time - started < duration)
            {
                var t = Mathf.Clamp01((Time.time - started) / duration);
                controller.rotation = Quaternion.Slerp(origin, target, t);
                yield return null;
            }
            controller.rotation = target;
        }

        public bool SelectEnter(GameObject controller, GameObject target, out string error)
        {
            error = null;
            var interactor = controller != null ? controller.GetComponentInParent<XRBaseInteractor>() : null;
            var interactable = target != null ? target.GetComponent<XRBaseInteractable>() : null;
            if (interactor == null || interactable == null)
            {
                error = "SelectEnter requires XRBaseInteractor on controller hierarchy and XRBaseInteractable on target.";
                return false;
            }

            var manager = interactor.interactionManager != null ? interactor.interactionManager : UnityEngine.Object.FindObjectOfType<XRInteractionManager>();
            if (manager == null)
            {
                error = "No XRInteractionManager found for SelectEnter.";
                return false;
            }

            manager.SelectEnter((IXRSelectInteractor)interactor, (IXRSelectInteractable)interactable);
            return true;
        }

        public bool SelectExit(GameObject controller, GameObject target, out string error)
        {
            error = null;
            var interactor = controller != null ? controller.GetComponentInParent<XRBaseInteractor>() : null;
            var interactable = target != null ? target.GetComponent<XRBaseInteractable>() : null;
            if (interactor == null || interactable == null)
            {
                error = "SelectExit requires XRBaseInteractor on controller hierarchy and XRBaseInteractable on target.";
                return false;
            }

            var manager = interactor.interactionManager != null ? interactor.interactionManager : UnityEngine.Object.FindObjectOfType<XRInteractionManager>();
            if (manager == null)
            {
                error = "No XRInteractionManager found for SelectExit.";
                return false;
            }

            manager.SelectExit((IXRSelectInteractor)interactor, (IXRSelectInteractable)interactable);
            return true;
        }

        public bool Activate(GameObject controller, GameObject target, out string error)
        {
            error = null;
            var interactor = controller != null ? controller.GetComponentInParent<XRBaseInteractor>() as IXRActivateInteractor : null;
            var interactable = target != null ? target.GetComponent<XRBaseInteractable>() : null;
            var activateInteractable = interactable as IXRActivateInteractable;
            if (interactor == null || activateInteractable == null)
            {
                error = "Activate requires IXRActivateInteractor on controller hierarchy and XRBaseInteractable on target.";
                return false;
            }

            activateInteractable.OnActivated(new ActivateEventArgs
            {
                interactorObject = interactor,
                interactableObject = activateInteractable
            });
            return true;
        }

        public bool Deactivate(GameObject controller, GameObject target, out string error)
        {
            error = null;
            var interactor = controller != null ? controller.GetComponentInParent<XRBaseInteractor>() as IXRActivateInteractor : null;
            var interactable = target != null ? target.GetComponent<XRBaseInteractable>() : null;
            var activateInteractable = interactable as IXRActivateInteractable;
            if (interactor == null || activateInteractable == null)
            {
                error = "Deactivate requires IXRActivateInteractor on controller hierarchy and XRBaseInteractable on target.";
                return false;
            }

            activateInteractable.OnDeactivated(new DeactivateEventArgs
            {
                interactorObject = interactor,
                interactableObject = activateInteractable
            });
            return true;
        }
    }

    public class AssertionEvaluator
    {
        public bool AssertEventFired(IEnumerable<XrObservation> observations, string eventType, GameObject target, out string message)
        {
            foreach (var observation in observations)
            {
                if (observation.eventType == eventType && (target == null || observation.objectName == target.name))
                {
                    message = $"Observed {eventType} on {observation.objectName}.";
                    return true;
                }
            }

            message = $"Did not observe {eventType}" + (target != null ? $" on {target.name}." : ".");
            return false;
        }

        public bool AssertTransformChanged(GameObject target, Vector3 startPosition, Quaternion startRotation, float positionTolerance, float angleTolerance, out string message)
        {
            if (target == null)
            {
                message = "AssertTransformChanged target was not resolved.";
                return false;
            }

            var moved = Vector3.Distance(target.transform.position, startPosition) > positionTolerance;
            var rotated = Quaternion.Angle(target.transform.rotation, startRotation) > angleTolerance;
            if (moved || rotated)
            {
                message = $"Transform changed. moved={moved}, rotated={rotated}.";
                return true;
            }

            message = $"Transform did not change beyond position tolerance {positionTolerance} or angle tolerance {angleTolerance}.";
            return false;
        }

        public bool AssertPropertyChanged(GameObject target, string componentName, string propertyName, string initialValue, out string message)
        {
            var current = ReadProperty(target, componentName, propertyName);
            if (current == null)
            {
                message = $"Property '{componentName}.{propertyName}' could not be read.";
                return false;
            }

            if (!string.Equals(initialValue, current, StringComparison.Ordinal))
            {
                message = $"Property changed from '{initialValue}' to '{current}'.";
                return true;
            }

            message = $"Property '{componentName}.{propertyName}' remained '{current}'.";
            return false;
        }

        public static string ReadProperty(GameObject target, string componentName, string propertyName)
        {
            if (target == null || string.IsNullOrEmpty(componentName) || string.IsNullOrEmpty(propertyName))
                return null;

            Component component = null;
            foreach (var candidate in target.GetComponents<Component>())
            {
                if (candidate == null)
                    continue;
                var type = candidate.GetType();
                if (type.Name == componentName || type.FullName == componentName)
                {
                    component = candidate;
                    break;
                }
            }

            if (component == null)
                return null;

            var componentType = component.GetType();
            var property = componentType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null)
                return Convert.ToString(property.GetValue(component, null), CultureInfo.InvariantCulture);

            var field = componentType.GetField(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
                return Convert.ToString(field.GetValue(component), CultureInfo.InvariantCulture);

            return null;
        }
    }

    public class ActionRunner
    {
        readonly ObjectResolver m_Resolver;
        readonly InteractorDriver m_Driver;
        readonly ObservationRecorder m_Recorder;
        readonly AssertionEvaluator m_Assertions;
        GameObject m_CurrentTarget;
        GameObject m_CurrentController;

        public XrExecutionResult LastResult { get; private set; }

        public ActionRunner(ObjectResolver resolver = null, InteractorDriver driver = null, ObservationRecorder recorder = null, AssertionEvaluator assertions = null)
        {
            m_Resolver = resolver ?? new ObjectResolver();
            m_Driver = driver ?? new InteractorDriver();
            m_Recorder = recorder ?? new ObservationRecorder();
            m_Assertions = assertions ?? new AssertionEvaluator();
        }

        public IEnumerator Execute(XrActionPlan plan)
        {
            LastResult = new XrExecutionResult
            {
                planId = plan != null ? plan.planId : null,
                schemaVersion = plan != null ? plan.schemaVersion : 0,
                status = XrActionStatus.Passed
            };

            if (plan == null)
            {
                LastResult.status = XrActionStatus.Failed;
                LastResult.warnings.Add("Cannot execute a null action plan.");
                yield break;
            }

            if (plan.schemaVersion != 1)
            {
                LastResult.status = XrActionStatus.Failed;
                LastResult.warnings.Add($"Unsupported Vektor action schema version {plan.schemaVersion}.");
                yield break;
            }

            foreach (var action in plan.actions)
            {
                var result = new XrActionResult
                {
                    actionId = action.id,
                    type = action.type,
                    startedAt = Time.time
                };
                LastResult.actions.Add(result);

                yield return ExecuteAction(action, result);

                result.endedAt = Time.time;
                result.elapsedSeconds = result.endedAt - result.startedAt;
                if (result.status != XrActionStatus.Passed)
                {
                    LastResult.status = result.status;
                    yield break;
                }
            }
        }

        IEnumerator ExecuteAction(XrActionSpec action, XrActionResult result)
        {
            var observationStart = m_Recorder.Count;
            var target = Resolve(action.target, result.warnings);
            var controller = Resolve(action.controller, result.warnings);

            if (target != null)
            {
                m_CurrentTarget = target;
                m_Recorder.Register(target);
            }
            else if (RequiresTarget(action.type))
            {
                target = m_CurrentTarget;
            }

            if (controller != null)
                m_CurrentController = controller;
            else if (RequiresController(action.type))
                controller = m_CurrentController;

            result.targetName = target != null ? target.name : null;
            result.controllerName = controller != null ? controller.name : null;

            var failed = false;
            var driverMode = ResolveDriverMode(action, out var explicitDriverMode);
            result.driverMode = driverMode.ToString();
            Func<string, bool> fail = message =>
            {
                failed = true;
                result.status = message.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0 ? XrActionStatus.TimedOut : XrActionStatus.Failed;
                result.failureCode = result.status.ToString();
                result.message = message;
                return false;
            };

            switch (action.type)
            {
                case XrActionKind.NavigateTo:
                    if (driverMode != XrDriverMode.DirectTransform)
                    {
                        fail("NavigateTo requires driverMode DirectTransform.");
                        break;
                    }
                    yield return m_Driver.NavigateTo(Required(controller, "controller/player", fail)?.transform, Required(target, "target", fail)?.transform, Float(action, "moveSpeed", 1.0f), Float(action, "interactionAngle", 5.0f), Float(action, "interactionDistance", 1.0f), action.timeoutSeconds, fail);
                    break;
                case XrActionKind.MoveControllerTo:
                    if (driverMode == XrDriverMode.XRDeviceSimulatorKeyboard)
                    {
                        if (controller == null || target == null)
                        {
                            fail("MoveControllerTo with XRDeviceSimulatorKeyboard requires resolved controller and target objects.");
                        }
                        else
                        {
                            yield return RunWithTimeout(TestLib.MoveControllerToObject(controller, target, Bool(action, "selectHeld", false)), action.timeoutSeconds, () => fail($"MoveControllerTo with XRDeviceSimulatorKeyboard timed out after {action.timeoutSeconds:0.###}s."));
                        }
                    }
                    else
                    {
                        if (driverMode != XrDriverMode.DirectTransform)
                        {
                            fail("MoveControllerTo requires driverMode DirectTransform or XRDeviceSimulatorKeyboard.");
                            break;
                        }
                        yield return m_Driver.MoveControllerTo(Required(controller, "controller", fail)?.transform, Required(target, "target", fail)?.transform, Float(action, "speed", 1.0f), Float(action, "threshold", 0.05f), action.timeoutSeconds, fail);
                    }
                    break;
                case XrActionKind.SelectEnter:
                    if (driverMode == XrDriverMode.XRDeviceSimulatorKeyboard)
                    {
                        yield return TestLib.Grab();
                    }
                    else if (driverMode != XrDriverMode.XRInteractionManager)
                    {
                        fail("SelectEnter requires driverMode XRInteractionManager or XRDeviceSimulatorKeyboard.");
                    }
                    else if (controller == null || target == null)
                    {
                        if (explicitDriverMode)
                            fail("SelectEnter with XRInteractionManager requires resolved controller and target objects.");
                        else
                            yield return WarnAndRunKeyboard(result, "SelectEnter did not have a resolved controller/target; falling back to keyboard grab.", TestLib.Grab());
                    }
                    else if (!m_Driver.SelectEnter(controller, target, out var selectEnterError))
                    {
                        if (explicitDriverMode)
                            fail(selectEnterError);
                        else
                            yield return WarnAndRunKeyboard(result, selectEnterError + " Falling back to keyboard grab.", TestLib.Grab());
                    }
                    break;
                case XrActionKind.SelectExit:
                    if (driverMode == XrDriverMode.XRDeviceSimulatorKeyboard)
                    {
                        yield return TestLib.ReleaseAllKeys();
                    }
                    else if (driverMode != XrDriverMode.XRInteractionManager)
                    {
                        fail("SelectExit requires driverMode XRInteractionManager or XRDeviceSimulatorKeyboard.");
                    }
                    else if (controller == null || target == null)
                    {
                        if (explicitDriverMode)
                            fail("SelectExit with XRInteractionManager requires resolved controller and target objects.");
                        else
                            yield return WarnAndRunKeyboard(result, "SelectExit did not have a resolved controller/target; falling back to keyboard release.", TestLib.ReleaseAllKeys());
                    }
                    else if (!m_Driver.SelectExit(controller, target, out var selectExitError))
                    {
                        if (explicitDriverMode)
                            fail(selectExitError);
                        else
                            yield return WarnAndRunKeyboard(result, selectExitError + " Falling back to keyboard release.", TestLib.ReleaseAllKeys());
                    }
                    break;
                case XrActionKind.Activate:
                    if (driverMode == XrDriverMode.XRDeviceSimulatorKeyboard)
                    {
                        yield return Bool(action, "selectHeld", false) ? TestLib.GrabHoldAndTrigger() : TestLib.Trigger();
                    }
                    else if (driverMode != XrDriverMode.XRInteractionManager)
                    {
                        fail("Activate requires driverMode XRInteractionManager or XRDeviceSimulatorKeyboard.");
                    }
                    else if (controller == null || target == null)
                    {
                        if (explicitDriverMode)
                            fail("Activate with XRInteractionManager requires resolved controller and target objects.");
                        else
                            yield return WarnAndRunKeyboard(result, "Activate did not have a resolved controller/target; falling back to keyboard.", Bool(action, "selectHeld", false) ? TestLib.GrabHoldAndTrigger() : TestLib.Trigger());
                    }
                    else if (!m_Driver.Activate(controller, target, out var activateError))
                    {
                        if (explicitDriverMode)
                            fail(activateError);
                        else
                            yield return WarnAndRunKeyboard(result, activateError + " Falling back to keyboard.", Bool(action, "selectHeld", false) ? TestLib.GrabHoldAndTrigger() : TestLib.Trigger());
                    }
                    break;
                case XrActionKind.Deactivate:
                    if (driverMode == XrDriverMode.XRDeviceSimulatorKeyboard)
                    {
                        yield return TestLib.ReleaseAllKeys();
                    }
                    else if (driverMode != XrDriverMode.XRInteractionManager)
                    {
                        fail("Deactivate requires driverMode XRInteractionManager or XRDeviceSimulatorKeyboard.");
                    }
                    else if (controller == null || target == null)
                    {
                        if (explicitDriverMode)
                            fail("Deactivate with XRInteractionManager requires resolved controller and target objects.");
                        else
                            yield return WarnAndRunKeyboard(result, "Deactivate did not have a resolved controller/target; falling back to keyboard release.", TestLib.ReleaseAllKeys());
                    }
                    else if (!m_Driver.Deactivate(controller, target, out var deactivateError))
                    {
                        if (explicitDriverMode)
                            fail(deactivateError);
                        else
                            yield return WarnAndRunKeyboard(result, deactivateError + " Falling back to keyboard release.", TestLib.ReleaseAllKeys());
                    }
                    break;
                case XrActionKind.MoveController:
                    if (driverMode == XrDriverMode.XRDeviceSimulatorKeyboard && TryParseKey(String(action, "keyFallback", null), out var moveKey))
                    {
                        yield return TestLib.MoveHold(moveKey, Float(action, "duration", 0.1f));
                    }
                    else
                    {
                        if (driverMode != XrDriverMode.DirectTransform)
                        {
                            fail("MoveController requires driverMode DirectTransform or XRDeviceSimulatorKeyboard with keyFallback.");
                            break;
                        }
                        yield return m_Driver.MoveController(Required(controller, "controller", fail)?.transform, Vector(action, "delta", Vector3.zero), Float(action, "duration", 0.1f));
                    }
                    break;
                case XrActionKind.RotateController:
                    if (driverMode == XrDriverMode.XRDeviceSimulatorKeyboard && TryParseKey(String(action, "keyFallback", null), out var rotateKey))
                    {
                        yield return TestLib.MoveHold(rotateKey, Float(action, "duration", 0.1f));
                    }
                    else
                    {
                        if (driverMode != XrDriverMode.DirectTransform)
                        {
                            fail("RotateController requires driverMode DirectTransform or XRDeviceSimulatorKeyboard with keyFallback.");
                            break;
                        }
                        yield return m_Driver.RotateController(Required(controller, "controller", fail)?.transform, Vector(action, "eulerDelta", Vector3.zero), Float(action, "duration", 0.1f));
                    }
                    break;
                case XrActionKind.Wait:
                    yield return new WaitForSeconds(Float(action, "duration", 0.1f));
                    break;
                case XrActionKind.AssertEventFired:
                    if (!m_Assertions.AssertEventFired(m_Recorder.Observations, String(action, "eventType", "selectEntered"), target, out var eventMessage))
                        fail(eventMessage);
                    else
                        result.message = eventMessage;
                    break;
                case XrActionKind.AssertTransformChanged:
                    if (!m_Assertions.AssertTransformChanged(Required(target, "target", fail), Vector(action, "startPosition", target != null ? target.transform.position : Vector3.zero), Quaternion.Euler(Vector(action, "startEuler", Vector3.zero)), Float(action, "positionTolerance", 0.001f), Float(action, "angleTolerance", 0.1f), out var transformMessage))
                        fail(transformMessage);
                    else
                        result.message = transformMessage;
                    break;
                case XrActionKind.AssertPropertyChanged:
                    if (!m_Assertions.AssertPropertyChanged(Required(target, "target", fail), String(action, "component", null), String(action, "property", null), String(action, "initialValue", null), out var propertyMessage))
                        fail(propertyMessage);
                    else
                        result.message = propertyMessage;
                    break;
                default:
                    fail($"Unsupported action kind {action.type}.");
                    break;
            }

            result.observations = m_Recorder.SnapshotSince(observationStart);
            if (!failed && result.status == XrActionStatus.Pending)
                result.status = XrActionStatus.Passed;
        }

        GameObject Resolve(XrObjectRef reference, List<string> warnings)
        {
            var obj = m_Resolver.Resolve(reference, warnings);
            if (obj != null)
                m_Recorder.Register(obj);
            return obj;
        }

        static IEnumerator WarnAndRunKeyboard(XrActionResult result, string warning, IEnumerator routine)
        {
            result.warnings.Add(warning);
            yield return routine;
        }

        static XrDriverMode ResolveDriverMode(XrActionSpec action, out bool explicitDriverMode)
        {
            explicitDriverMode = action.driverMode.HasValue;

            if (Bool(action, "keyboardFallback", false))
                return XrDriverMode.XRDeviceSimulatorKeyboard;

            if (action.driverMode.HasValue)
                return action.driverMode.Value;

            if (action.type == XrActionKind.SelectEnter || action.type == XrActionKind.SelectExit ||
                action.type == XrActionKind.Activate || action.type == XrActionKind.Deactivate)
            {
                return XrDriverMode.XRInteractionManager;
            }

            return XrDriverMode.DirectTransform;
        }

        IEnumerator RunWithTimeout(IEnumerator routine, float timeoutSeconds, Action onTimeout)
        {
            var started = Time.time;
            while (routine != null)
            {
                if (Time.time - started > timeoutSeconds)
                {
                    onTimeout?.Invoke();
                    yield break;
                }

                if (!routine.MoveNext())
                    yield break;

                yield return routine.Current;
            }
        }

        static GameObject Required(GameObject obj, string role, Func<string, bool> fail)
        {
            if (obj == null)
                fail($"Required {role} was not resolved.");
            return obj;
        }

        static bool RequiresTarget(XrActionKind kind)
        {
            return kind == XrActionKind.SelectEnter || kind == XrActionKind.SelectExit || kind == XrActionKind.Activate ||
                kind == XrActionKind.Deactivate || kind == XrActionKind.AssertPropertyChanged ||
                kind == XrActionKind.AssertEventFired || kind == XrActionKind.AssertTransformChanged;
        }

        static bool RequiresController(XrActionKind kind)
        {
            return kind == XrActionKind.NavigateTo || kind == XrActionKind.MoveControllerTo ||
                kind == XrActionKind.SelectEnter || kind == XrActionKind.SelectExit ||
                kind == XrActionKind.Activate || kind == XrActionKind.Deactivate ||
                kind == XrActionKind.MoveController || kind == XrActionKind.RotateController;
        }

        static string String(XrActionSpec action, string key, string fallback)
        {
            if (action.parameters != null && action.parameters.TryGetValue(key, out var value))
                return value;
            return fallback;
        }

        static float Float(XrActionSpec action, string key, float fallback)
        {
            if (action.parameters != null && action.parameters.TryGetValue(key, out var value) && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
            return fallback;
        }

        static bool Bool(XrActionSpec action, string key, bool fallback)
        {
            if (action.parameters != null && action.parameters.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed))
                return parsed;
            return fallback;
        }

        static Vector3 Vector(XrActionSpec action, string key, Vector3 fallback)
        {
            if (action.parameters == null || !action.parameters.TryGetValue(key, out var value) || string.IsNullOrEmpty(value))
                return fallback;

            var pieces = value.Split(',');
            if (pieces.Length != 3)
                return fallback;

            if (float.TryParse(pieces[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                float.TryParse(pieces[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
                float.TryParse(pieces[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
            {
                return new Vector3(x, y, z);
            }

            return fallback;
        }

        static bool TryParseKey(string value, out Key key)
        {
            if (!string.IsNullOrEmpty(value) && Enum.TryParse(value, out key))
                return true;

            key = Key.None;
            return false;
        }
    }

#if UNITY_EDITOR
    public static class VektorIdSeeder
    {
        [UnityEditor.MenuItem("Tools/Vektor/Seed Missing IDs")]
        public static void SeedMissingIds()
        {
            var count = 0;
            foreach (var interactable in UnityEngine.Object.FindObjectsOfType<XRBaseInteractable>(true))
            {
                var marker = interactable.GetComponent<VektorId>();
                if (marker == null)
                {
                    marker = UnityEditor.Undo.AddComponent<VektorId>(interactable.gameObject);
                    count++;
                }
                marker.EnsureId();
                UnityEditor.EditorUtility.SetDirty(marker);
            }
            Debug.Log($"Vektor seeded IDs on {count} interactables.");
        }
    }
#endif
}
