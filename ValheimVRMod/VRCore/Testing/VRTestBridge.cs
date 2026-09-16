using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using ValheimVRMod.Utilities;

using static ValheimVRMod.Utilities.LogUtils;

namespace ValheimVRMod.VRCore.Testing
{
    /// <summary>
    /// Local-only telemetry for automated VR testing. This first version is intentionally read-only:
    /// test input will later use a dedicated provider instead of changing live SteamVR action state.
    /// </summary>
    internal sealed class VRTestBridge : MonoBehaviour
    {
        private const string LOCALHOST_PREFIX = "http://127.0.0.1:";
        private const float FPS_SAMPLE_PERIOD = 0.5f;

        private HttpListener listener;
        private Thread listenerThread;
        private volatile bool running;
        private volatile string currentState = "{\"xrSession\":\"INITIALIZING\"}";
        private float frameTimeSum;
        private int frameCount;
        private float sampledFps;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            StartListener();
        }

        private void Update()
        {
            SampleFps();
            currentState = CreateStateJson();
        }

        private void OnDestroy()
        {
            StopListener();
        }

        private void StartListener()
        {
            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add(LOCALHOST_PREFIX + VHVRConfig.TestBridgePort() + "/");
                listener.Start();
                running = true;
                listenerThread = new Thread(Listen) { IsBackground = true, Name = "VHVR Test Bridge" };
                listenerThread.Start();
                LogInfo("VR test bridge listening on " + LOCALHOST_PREFIX + VHVRConfig.TestBridgePort() + "/vr/state");
            }
            catch (Exception exception)
            {
                LogError("Could not start VR test bridge: " + exception.Message);
                StopListener();
            }
        }

        private void StopListener()
        {
            running = false;
            try { listener?.Stop(); } catch { }
            try { listener?.Close(); } catch { }
            listener = null;
        }

        private void Listen()
        {
            while (running && listener != null)
            {
                try
                {
                    HandleRequest(listener.GetContext());
                }
                catch (HttpListenerException) when (!running)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception exception)
                {
                    LogWarning("VR test bridge request failed: " + exception.Message);
                }
            }
        }

        private void HandleRequest(HttpListenerContext context)
        {
            string path = context.Request.Url.AbsolutePath;
            string body;
            int status;
            if (context.Request.HttpMethod == "GET" && path == "/vr/state")
            {
                body = currentState;
                status = 200;
            }
            else if (context.Request.HttpMethod == "GET" && path == "/vr/health")
            {
                body = "{\"status\":\"ok\",\"bridge\":\"telemetry-v1\"}";
                status = 200;
            }
            else
            {
                body = "{\"error\":\"Available endpoints: GET /vr/health, GET /vr/state\"}";
                status = 404;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(body);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            using (Stream output = context.Response.OutputStream)
            {
                output.Write(bytes, 0, bytes.Length);
            }
        }

        private void SampleFps()
        {
            frameTimeSum += Time.unscaledDeltaTime;
            frameCount++;
            if (frameTimeSum >= FPS_SAMPLE_PERIOD)
            {
                sampledFps = frameCount / frameTimeSum;
                frameTimeSum = 0f;
                frameCount = 0;
            }
        }

        private string CreateStateJson()
        {
            Camera camera = VRPlayer.vrCam;
            bool hmdTracked = camera != null && XRSettings.isDeviceActive;
            bool leftTracked = VRPlayer.leftHand != null && VRPlayer.leftHand.gameObject.activeInHierarchy;
            bool rightTracked = VRPlayer.rightHand != null && VRPlayer.rightHand.gameObject.activeInHierarchy;
            Vector3 headPose = camera != null ? camera.transform.position : Vector3.zero;
            string session = !XRSettings.isDeviceActive ? "IDLE" : (Application.isFocused ? "FOCUSED" : "VISIBLE");
            string scene = SceneManager.GetActiveScene().name ?? string.Empty;

            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{{\"xrSession\":\"{0}\",\"hmdTracked\":{1},\"leftControllerTracked\":{2},\"rightControllerTracked\":{3}," +
                "\"headPose\":{{\"x\":{4:F3},\"y\":{5:F3},\"z\":{6:F3}}},\"fps\":{7:F1},\"currentScene\":\"{8}\",\"vrCameraActive\":{9}}}",
                session, ToJsonBool(hmdTracked), ToJsonBool(leftTracked), ToJsonBool(rightTracked),
                headPose.x, headPose.y, headPose.z, sampledFps, Escape(scene), ToJsonBool(camera != null && camera.isActiveAndEnabled));
        }

        private static string ToJsonBool(bool value)
        {
            return value ? "true" : "false";
        }

        private static string Escape(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
