using UnityEngine;
namespace FriendSlop.Presentation { public sealed partial class AvatarVisual {
        Mesh _cueSleeveMesh;
        GameObject _cueSleeveObject;
        readonly Vector3[] _cueSleeveVertices = new Vector3[7 * 16];
        void UpdateCueSleeve(bool active)
        {
            if (_cueSleeveObject == null && active)
            {
                _cueSleeveObject = new GameObject("CueSleeveCurve");
                _cueSleeveObject.transform.SetParent(transform, false);
                _cueSleeveMesh = new Mesh { name = "CueSleeveCurve" };
                _cueSleeveMesh.MarkDynamic();
                _cueSleeveObject.AddComponent<MeshFilter>().sharedMesh = _cueSleeveMesh;
                var sleeveRenderer = _cueSleeveObject.AddComponent<MeshRenderer>(); sleeveRenderer.sharedMaterial = _cueWristJoint.GetComponent<Renderer>().sharedMaterial; _renderers.Add(sleeveRenderer); ApplyRenderMode();
                var indices = new int[6 * 16 * 6]; int n = 0;
                for (int ring = 0; ring < 6; ring++) for (int side = 0; side < 16; side++)
                {
                    int a = ring * 16 + side, b = ring * 16 + (side + 1) % 16;
                    indices[n++] = a; indices[n++] = b; indices[n++] = a + 16;
                    indices[n++] = b; indices[n++] = b + 16; indices[n++] = a + 16;
                }
                _cueSleeveMesh.vertices = _cueSleeveVertices;
                _cueSleeveMesh.triangles = indices;
            }
            if (_cueSleeveObject == null) return;
            _cueSleeveObject.SetActive(active);
            if (!active) return;
            Vector3 p0 = _elbowR.TransformPoint(new Vector3(0, -.115f, 0));
            Vector3 p3 = _handR.TransformPoint(Vector3.up * .005f);
            float span = Vector3.Distance(p0, p3);
            Vector3 p1 = p0 - _elbowR.up * span * .4f;
            Vector3 p2 = p3 + _handR.up * span * .4f;
            for (int ring = 0; ring <= 6; ring++)
            {
                float t = ring / 6f, u = 1 - t;
                Vector3 center = u*u*u*p0 + 3*u*u*t*p1 + 3*u*t*t*p2 + t*t*t*p3;
                Vector3 tangent = (3*u*u*(p1-p0) + 6*u*t*(p2-p1) + 3*t*t*(p3-p2)).normalized;
                Vector3 axis = Vector3.ProjectOnPlane(_handR.right, tangent).normalized;
                if (axis.sqrMagnitude < .01f) axis = Vector3.Cross(tangent, Vector3.forward).normalized;
                Vector3 other = Vector3.Cross(tangent, axis);
                float radius = Mathf.Lerp(.042f, .043f, t);
                for (int side = 0; side < 16; side++)
                {
                    float angle = side * Mathf.PI * 2 / 16;
                    _cueSleeveVertices[ring*16+side] = transform.InverseTransformPoint(center + radius * (axis*Mathf.Cos(angle) + other*Mathf.Sin(angle)));
                }
            }
            _cueSleeveMesh.vertices = _cueSleeveVertices;
            _cueSleeveMesh.RecalculateNormals(); _cueSleeveMesh.RecalculateBounds();
        }

        void ReleaseCueSleeve()
        {
            if (_cueSleeveObject != null)
            {
                var renderer = _cueSleeveObject.GetComponent<Renderer>();
                _renderers.Remove(renderer); _normalMaterials.Remove(renderer);
                _cueSleeveObject.SetActive(false); Release(_cueSleeveObject);
            }
            Release(_cueSleeveMesh); _cueSleeveMesh = null; _cueSleeveObject = null;
        }
} }


