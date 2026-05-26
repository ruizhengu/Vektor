# Vektor

Vektor is a deterministic XR action runtime for Unity. It gives autonomous XR test agents a small, structured way to drive scenes: resolve objects, execute actions, record observations, run assertions, handle timeouts, and return machine-readable results.

Vektor is the productized successor to the IntenXion research prototype. IntenXion stays useful as an archived Unity sample project; Vektor is the clean package that new projects should depend on.

## Who It Is For

- XR teams that want repeatable PlayMode interaction tests.
- Agent systems, such as Voqal, that need to emit JSON action plans instead of ad hoc C#.
- Unity projects migrating from IntenXion-style helper tests to a schema-first runtime.

## Install

In Unity Package Manager, add this package from disk or by Git URL. For local development:

```text
file:/absolute/path/to/Vektor
```

Required Unity packages:

- Input System
- XR Interaction Toolkit
- Unity Newtonsoft JSON

The package includes runtime code under `Runtime/` and focused PlayMode tests under `Tests/`.

## Quick Start

For hand-written PlayMode tests, use the compatibility fluent API:

```csharp
using Vektor;
using static Vektor.TestLib;

yield return new ActionBuilder()
    .NavigateTo(origin, cube)
    .MoveControllerTo(rightController, cube)
    .GrabHold(1.0f)
    .MoveUp(0.5f)
    .ReleaseAllKeys()
    .Execute();
```

The fluent API builds an `XrActionPlan` internally, so generated JSON plans and hand-written tests share the same runner.

## Manual PlayMode Tests

Most Unity users will start with hand-written PlayMode tests. The pattern is:

1. Load the scene.
2. Find the XR Origin and controller.
3. Find the object under test.
4. Navigate, move the controller, grab/trigger/move, then assert the outcome.

```csharp
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Vektor;
using static Vektor.TestLib;

public class XrInteractionTests
{
    GameObject origin;
    GameObject rightController;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        yield return SceneManager.LoadSceneAsync("EvaluationScene", LoadSceneMode.Single);
        yield return new WaitForSeconds(0.1f);

        origin = GameObject.Find("XR Origin (XR Rig)");
        rightController = GameObject.Find("Right Controller");

        Assert.IsNotNull(origin);
        Assert.IsNotNull(rightController);
    }

    [UnityTest]
    public IEnumerator GrabAndTriggerObject()
    {
        var target = FindXRObject("Blaster Variant");
        Assert.IsNotNull(target);

        yield return new ActionBuilder()
            .NavigateTo(origin, target)
            .MoveControllerTo(rightController, target)
            .GrabHold(1.0f)
            .Trigger()
            .ReleaseAllKeys()
            .Execute();

        AssertGrabbed(target, "Object should have been grabbed");
        AssertTriggered(target, "Object should have been triggered");
    }
}
```

For controls such as sliders, levers, dials, doors, and drawers, capture the initial value or transform, perform a grab and movement sequence, then assert that the value changed:

```csharp
[UnityTest]
public IEnumerator MoveAHandle()
{
    var handle = FindXRObject("Drawer Handle", true);
    Assert.IsNotNull(handle);

    var startPosition = handle.transform.position;

    yield return new ActionBuilder()
        .NavigateTo(origin, handle)
        .MoveControllerTo(rightController, handle)
        .GrabHold(1.0f)
        .MoveLeft(1.0f)
        .ReleaseAllKeys()
        .Execute();

    yield return new WaitForSeconds(0.5f);
    AssertTranslated(handle, startPosition, "Handle should have moved");
}
```

For generated plans, prefer JSON:

```json
{
  "schemaVersion": 1,
  "planId": "grab-cube",
  "actions": [
    {
      "id": "move-controller",
      "type": "MoveControllerTo",
      "driverMode": "DirectTransform",
      "target": { "vektorId": "cube-01", "name": "Cube Interactable" },
      "controller": { "name": "Right Controller" },
      "timeoutSeconds": 5
    },
    {
      "id": "grab",
      "type": "SelectEnter",
      "driverMode": "XRInteractionManager",
      "timeoutSeconds": 2
    }
  ]
}
```

Execute a JSON plan from a PlayMode test:

```csharp
var runner = new ActionRunner();
yield return runner.Execute(XrActionPlan.FromJson(json));

Assert.IsTrue(runner.LastResult.Succeeded, runner.LastResult.ToJson());
```

## Driver Modes

Every generated action should choose a driver mode:

- `DirectTransform`: deterministic transform movement for navigation and controller positioning.
- `XRInteractionManager`: interaction through XR Interaction Toolkit managers, interactors, and interactables.
- `XRDeviceSimulatorKeyboard`: compatibility path for scenes driven by Unity's XR Device Simulator keyboard controls.

Legacy plans that use `parameters.keyboardFallback` still work. New Voqal-generated plans should use `driverMode` directly.

## Stable Object References

Generated plans should identify scene objects with `VektorId`:

```json
{ "vektorId": "cube-01" }
```

Resolution order is:

1. `vektorId`
2. `intenXionId` compatibility fallback
3. `scenePath`
4. `name`

Name lookup is intentionally last because duplicate names are common in Unity scenes. Use `Tools > Vektor > Seed Missing IDs` in the Unity editor to add IDs to XR interactables.

## Results

`ActionRunner` returns an `XrExecutionResult` with:

- plan status and schema version
- per-action status, elapsed time, driver mode, target/controller names
- warnings for fallback behavior
- structured failure codes/messages
- observations captured from XR interaction events and transform records

Serialize it with:

```csharp
Debug.Log(runner.LastResult.ToJson());
```

## Repository Layout

```text
Runtime/
  ActionBuilder.cs        # compatibility fluent API
  TestLib.cs              # XR Device Simulator keyboard helpers
  VektorRuntime.cs        # schema, resolver, runner, observations, assertions
Tests/
  VektorRuntimeTests.cs   # focused Unity runtime tests
```

This repo intentionally does not contain the old IntenXion sample project. Keep sample scenes and evaluation scenes outside Vektor so the package stays small and deterministic.
