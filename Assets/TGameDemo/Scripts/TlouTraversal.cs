using System.Collections;
using UnityEngine;

namespace GameDemo
{
    /// <summary>
    /// Vault mid cover, ledge-climb tall boxes, crawl-space camera/stance.
    /// </summary>
    public class TlouTraversal : MonoBehaviour
    {
        public TlouPlayerMotor motor;
        public TlouCover cover;
        public TlouCamera cam;
        public float vaultMaxHeight = 1.25f;
        public float vaultMinHeight = 0.55f;
        public float ledgeReach = 1.15f;
        public float climbUpOffset = 0.15f;
        public LayerMask mask = ~0;

        public bool IsBusy { get; private set; }
        public bool InCrawlSpace { get; private set; }

        CrawlSpace _activeCrawl;

        void Update()
        {
            if (motor == null || IsBusy || motor.MovementLocked)
            {
                return;
            }

            if (!GameInput.JumpPressed && !GameInput.InteractPressed)
            {
                return;
            }

            if (TryVaultOrClimb())
            {
                return;
            }
        }

        bool TryVaultOrClimb()
        {
            Vector3 origin = transform.position + Vector3.up * 0.5f;
            Vector3 flatLook = cam != null
                ? Quaternion.Euler(0f, cam.Yaw, 0f) * Vector3.forward
                : transform.forward;
            flatLook.y = 0f;
            flatLook.Normalize();

            if (!Physics.Raycast(origin, flatLook, out RaycastHit hit, 1.1f, mask, QueryTriggerInteraction.Ignore))
            {
                // Also try cover surface we're on.
                if (cover != null && cover.IsInCover && cover.Current != null)
                {
                    return StartAgainstCover(cover.Current, -cover.Current.OutwardNormal(transform.position));
                }

                return false;
            }

            CoverSurface surf = hit.collider.GetComponentInParent<CoverSurface>();
            if (surf == null)
            {
                return false;
            }

            return StartAgainstCover(surf, hit.normal);
        }

        bool StartAgainstCover(CoverSurface surf, Vector3 hitNormal)
        {
            float h = surf.Height;

            // Mid cover → vault over
            if (surf.kind == CoverSurface.CoverKind.Mid && surf.allowVault &&
                h >= vaultMinHeight && h <= vaultMaxHeight)
            {
                StartCoroutine(VaultRoutine(surf, hitNormal));
                return true;
            }

            // Tall → ledge climb onto top
            if (surf.allowLedgeClimb && h > vaultMaxHeight && h < 3.2f)
            {
                // Must be close to top / in air or standing at face
                float topY = surf.TopY;
                bool canReach = transform.position.y + ledgeReach >= topY - 0.35f || motor.Grounded;
                if (canReach)
                {
                    StartCoroutine(LedgeClimbRoutine(surf, hitNormal));
                    return true;
                }
            }

            return false;
        }

        IEnumerator VaultRoutine(CoverSurface surf, Vector3 hitNormal)
        {
            IsBusy = true;
            motor.MovementLocked = true;
            if (cover != null && cover.IsInCover)
            {
                cover.ExitCover();
            }

            Vector3 n = hitNormal;
            n.y = 0f;
            n.Normalize();
            Bounds b = surf.Col.bounds;
            Vector3 start = transform.position;
            Vector3 over = b.center + n * -(b.extents.x + b.extents.z) * 0.15f;
            // Better: land on far side.
            Vector3 far = ClosestOnFarSide(surf, n);
            Vector3 mid = new Vector3((start.x + far.x) * 0.5f, b.max.y + 0.35f, (start.z + far.z) * 0.5f);
            far.y = start.y;

            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime * 2.4f;
                float s = Mathf.SmoothStep(0f, 1f, t);
                Vector3 a = Vector3.Lerp(start, mid, s);
                Vector3 c = Vector3.Lerp(mid, far, s);
                Vector3 p = Vector3.Lerp(a, c, s);
                motor.Teleport(p);
                yield return null;
            }

            motor.Teleport(far);
            motor.MovementLocked = false;
            IsBusy = false;
        }

        IEnumerator LedgeClimbRoutine(CoverSurface surf, Vector3 hitNormal)
        {
            IsBusy = true;
            motor.MovementLocked = true;
            if (cover != null && cover.IsInCover)
            {
                cover.ExitCover();
            }

            Vector3 n = hitNormal;
            n.y = 0f;
            n.Normalize();
            Bounds b = surf.Col.bounds;
            Vector3 hang = new Vector3(transform.position.x, b.max.y - 0.1f, transform.position.z);
            // Pull toward face then up onto top.
            Vector3 face = surf.ClosestPoint(transform.position + Vector3.up);
            face.y = b.max.y - 0.05f;
            Vector3 top = face - n * 0.55f;
            top.y = b.max.y + climbUpOffset;

            Vector3 start = transform.position;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime * 2.8f;
                motor.Teleport(Vector3.Lerp(start, face, Mathf.SmoothStep(0f, 1f, t)));
                yield return null;
            }

            t = 0f;
            start = transform.position;
            while (t < 1f)
            {
                t += Time.deltaTime * 2.6f;
                motor.Teleport(Vector3.Lerp(start, top, Mathf.SmoothStep(0f, 1f, t)));
                yield return null;
            }

            motor.Teleport(top);
            motor.MovementLocked = false;
            IsBusy = false;
        }

        Vector3 ClosestOnFarSide(CoverSurface surf, Vector3 inboundNormal)
        {
            Bounds b = surf.Col.bounds;
            Vector3 far = b.center - inboundNormal.normalized * (Mathf.Max(b.extents.x, b.extents.z) + 0.65f);
            far.y = transform.position.y;
            return far;
        }

        public void EnterCrawl(CrawlSpace space)
        {
            _activeCrawl = space;
            InCrawlSpace = true;
            motor.TrySetStance(PlayerStance.Prone);
        }

        public void ExitCrawl(CrawlSpace space)
        {
            if (_activeCrawl != space)
            {
                return;
            }

            _activeCrawl = null;
            InCrawlSpace = false;
        }
    }
}
