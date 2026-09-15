using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ValheimVRMod.VRCore.UI
{
    // Bends the camera HUD very slightly around the player. It operates on existing UI meshes,
    // so health, stamina, status effects and their animations retain their vanilla behaviour.
    [RequireComponent(typeof(Graphic))]
    public class CurvedHudGraphic : BaseMeshEffect
    {
        private const float CURVE_RADIUS_METERS = 0.35f;
        private Transform canvasTransform;
        private readonly List<UIVertex> vertices = new List<UIVertex>();

        public void SetCanvas(Transform value)
        {
            canvasTransform = value;
            graphic?.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vertexHelper)
        {
            if (!IsActive() || canvasTransform == null || vertexHelper.currentVertCount == 0)
            {
                return;
            }

            var canvasScale = Mathf.Abs(canvasTransform.lossyScale.x);
            if (canvasScale < Mathf.Epsilon)
            {
                return;
            }

            var radius = CURVE_RADIUS_METERS / canvasScale;
            vertices.Clear();
            vertexHelper.GetUIVertexStream(vertices);
            for (var i = 0; i < vertices.Count; i++)
            {
                var vertex = vertices[i];
                var canvasPosition = canvasTransform.InverseTransformPoint(transform.TransformPoint(vertex.position));
                var normalizedX = Mathf.Clamp(canvasPosition.x / radius, -0.95f, 0.95f);
                canvasPosition.z += radius * (Mathf.Sqrt(1f - normalizedX * normalizedX) - 1f);
                vertex.position = transform.InverseTransformPoint(canvasTransform.TransformPoint(canvasPosition));
                vertices[i] = vertex;
            }
            vertexHelper.Clear();
            vertexHelper.AddUIVertexTriangleStream(vertices);
        }
    }
}
