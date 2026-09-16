# VHVR Test Bridge — telemetry v1

`VRTestBridge` is a localhost-only telemetry endpoint for `TTN VR Debug`.
It never injects tracking or input in this version.

## Enable it

In the debug profile's `org.bepinex.plugins.valheimvrmod.cfg`:

```ini
[Debug]
EnableTestBridge = true
TestBridgePort = 47373
```

## Endpoints

```text
GET http://127.0.0.1:47373/vr/health
GET http://127.0.0.1:47373/vr/state
```

`/vr/state` includes inferred XR session state, HMD/controller availability, head-camera
position, sampled FPS, scene name, and VR-camera state.

## Next layers

1. Capture the HMD render target.
2. Add a dedicated test input/tracking provider for deterministic poses and buttons.

The future input provider must not overwrite live SteamVR actions. It will be enabled
only in the debug profile.
