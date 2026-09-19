using UnityEngine;

namespace GameDemo
{
    /// <summary>
    /// Marks a collider as usable cover.
    /// Mid  = hip height. Crouch behind it, peek over the top, vault it.
    /// Tall = full wall. Hug it only near a corner, then lean around the edge.
    /// </summary>
    public class CoverSurface : MonoBehaviour
    {
        public enum CoverKind
        {
            Mid,
            Tall
        }

        public CoverKind kind = CoverKind.Tall;
        public bool allowVault = true;
        public bool allowLedgeClimb = true;
        public bool allowPeekOver = true;

        public Collider Col { get; private set; }

        void Awake()
        {
            Col = GetComponent<Collider>();
            if (Col == null)
            {
                Col = gameObject.AddComponent<BoxCollider>();
            }
        }

        void OnEnable()
        {
            if (Col == null)
            {
                Col = GetComponent<Collider>();
            }
        }

        public Vector3 ClosestPoint(Vector3 world)
        {
            return Col.ClosestPoint(world);
        }

        public Vector3 OutwardNormal(Vector3 fromPoint)
        {
            Vector3 closest = ClosestPoint(fromPoint);
            Vector3 n = fromPoint - closest;
            n.y = 0f;
            if (n.sqrMagnitude < 0.0001f)
            {
                n = -transform.forward;
                n.y = 0f;
            }

            return n.normalized;
        }

        /// <summary>
        /// Point on the cover face for the given outward normal, at the supplied height.
        /// Used to slide along a face without drifting off it.
        /// </summary>
        public Vector3 FacePlanePoint(Vector3 outwardNormal, float worldY)
        {
            Bounds b = Col.bounds;
            Vector3 p = b.center;
            if (Mathf.Abs(outwardNormal.x) > Mathf.Abs(outwardNormal.z))
            {
                p.x = outwardNormal.x > 0f ? b.max.x : b.min.x;
            }
            else
            {
                p.z = outwardNormal.z > 0f ? b.max.z : b.min.z;
            }

            p.y = worldY;
            return p;
        }

        public float TopY => Col.bounds.max.y;
        public float Height => Col.bounds.size.y;
    }
}
