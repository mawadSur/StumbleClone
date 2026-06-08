using UnityEngine;

namespace StumbleClone.Obstacles
{
    /// Kinematic block that shoots straight in from an edge, sweeping a lane across the
    /// arena, then despawns once it has travelled past the far side. Uses a trigger and
    /// manual knockback (kinematic-vs-kinematic contact wouldn't push on its own), so the
    /// shove is always in the ram's travel direction.
    ///
    /// Visual restyle is applied at runtime (in OnEnable) by recolouring the body near-black
    /// and bolting procedural cone spikes onto the left and right faces. This restyles
    /// instances already baked into scenes (Race track and others) with no re-bake, and never
    /// touches the collider, motion, or push behaviour.
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public sealed class SlidingRam : ArenaObstacle
    {
        // ---- Visual tuning (cosmetic only; does NOT affect the hit volume) ----
        private static readonly Color BodyColor = new Color(0.06f, 0.06f, 0.07f);   // near-black
        private static readonly Color SpikeColor = new Color(0.62f, 0.64f, 0.68f);  // light grey / metal
        private const int SpikesPerFace = 2;       // two spikes on each side face
        private const float SpikeLengthFrac = 0.55f;   // spike length as fraction of box X half-extent (added beyond the face)
        private const float SpikeRadiusFrac = 0.22f;   // spike base radius as fraction of the smaller cross dimension
        private const int SpikeSides = 10;             // cone base polygon resolution
        private const string SpikeRootName = "RamSpikes";

        private Rigidbody _rb;
        private Vector3 _travelDir;
        private float _speed;
        private float _maxTravel;
        private float _travelled;
        private bool _styled;

        protected override void OnEnable()
        {
            base.OnEnable();
            _rb = GetComponent<Rigidbody>();
            _rb.isKinematic = true;
            _rb.useGravity = false;

            ApplyVisualRestyle();
        }

        public override void Configure(Transform arenaCenter, float speedScale, float forceScale)
        {
            base.Configure(arenaCenter, speedScale, forceScale);

            Vector3 toCenter = arenaCenter != null
                ? (arenaCenter.position - transform.position)
                : transform.forward;
            toCenter.y = 0f;
            if (toCenter.sqrMagnitude < 0.0001f) toCenter = transform.forward;
            _travelDir = toCenter.normalized;

            transform.rotation = Quaternion.LookRotation(_travelDir, Vector3.up);
            _speed = 11f * Mathf.Max(0.5f, speedScale);
            // Travel across the whole diameter plus margin, then retire.
            _maxTravel = (arenaCenter != null ? toCenter.magnitude : 20f) * 2f + 6f;
        }

        protected override void Update()
        {
            base.Update();
            if (_rb == null) return;

            float step = _speed * Time.deltaTime;
            _rb.MovePosition(_rb.position + _travelDir * step);
            _travelled += step;
            if (_travelled >= _maxTravel) Despawn();
        }

        protected override Vector3 ComputePushDirection(Vector3 racerPosition)
        {
            // Always shove in the direction the ram is travelling.
            return _travelDir;
        }

        // -------------------------------------------------------------------------
        // Cosmetic restyle. Runs once per instance; safe to call repeatedly.
        // -------------------------------------------------------------------------

        private void ApplyVisualRestyle()
        {
            if (_styled) return;
            _styled = true;

            // 1) Recolour the body near-black (replacing any red material) via RuntimeMaterial.
            RecolorBody();

            // 2) Determine the box's local half-extents (from the BoxCollider, the source of
            //    truth for the hit volume) so spikes sit exactly on the +X / -X faces.
            if (!TryGetLocalBox(out Vector3 center, out Vector3 halfExtents)) return;

            // Don't double-build if a spike root already exists (re-enable / domain reload).
            if (transform.Find(SpikeRootName) != null) return;

            BuildSpikes(center, halfExtents);
        }

        private void RecolorBody()
        {
            // Apply to this object's renderer (the box body) and any direct mesh children
            // that aren't spikes, so the whole ram reads black.
            var bodyRenderer = GetComponent<MeshRenderer>();
            if (bodyRenderer != null)
            {
                RuntimeMaterial.Apply(gameObject, BodyColor);
            }
        }

        private bool TryGetLocalBox(out Vector3 center, out Vector3 halfExtents)
        {
            center = Vector3.zero;
            halfExtents = Vector3.zero;

            var box = GetComponent<BoxCollider>();
            if (box != null)
            {
                center = box.center;
                halfExtents = Vector3.Scale(box.size, Vector3.one) * 0.5f;
                if (halfExtents.sqrMagnitude > 0.0001f) return true;
            }

            // Fallback: derive from the mesh filter's local bounds.
            var mf = GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                Bounds b = mf.sharedMesh.bounds;
                center = b.center;
                halfExtents = b.extents;
                return halfExtents.sqrMagnitude > 0.0001f;
            }

            return false;
        }

        private void BuildSpikes(Vector3 center, Vector3 halfExtents)
        {
            var root = new GameObject(SpikeRootName);
            root.transform.SetParent(transform, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            float spikeLength = Mathf.Max(0.05f, halfExtents.x * SpikeLengthFrac);
            float crossMin = Mathf.Max(halfExtents.y, halfExtents.z) > 0f
                ? Mathf.Min(halfExtents.y, halfExtents.z)
                : halfExtents.x;
            float spikeRadius = Mathf.Max(0.03f, crossMin * SpikeRadiusFrac);

            Mesh coneMesh = BuildConeMesh(spikeRadius, spikeLength, SpikeSides);
            Material spikeMat = RuntimeMaterial.Make(SpikeColor);

            // Two spikes per face, spread along the box's local Z (its length axis).
            float zSpan = halfExtents.z * 0.9f;
            float[] zOffsets = SpikesPerFace == 1
                ? new[] { 0f }
                : new[] { -zSpan * 0.5f, zSpan * 0.5f };

            // +X face: spikes point toward +X. -X face: spikes point toward -X.
            CreateFaceSpikes(root.transform, coneMesh, spikeMat, center, halfExtents.x, +1f, zOffsets);
            CreateFaceSpikes(root.transform, coneMesh, spikeMat, center, halfExtents.x, -1f, zOffsets);
        }

        private void CreateFaceSpikes(Transform parent, Mesh coneMesh, Material mat,
            Vector3 boxCenter, float halfX, float xSign, float[] zOffsets)
        {
            // The cone mesh is built pointing along +Y; rotate so its tip faces along ±X.
            Quaternion rot = Quaternion.LookRotation(new Vector3(xSign, 0f, 0f), Vector3.up)
                             * Quaternion.FromToRotation(Vector3.up, Vector3.forward);

            for (int i = 0; i < zOffsets.Length; i++)
            {
                var spike = new GameObject($"Spike_{(xSign > 0f ? "PX" : "NX")}_{i}");
                spike.transform.SetParent(parent, false);
                spike.transform.localRotation = rot;
                // Seat the cone base on the face; the mesh's base sits at local Y=0 and the
                // tip at Y=length, so after rotation the base lands on the face plane.
                spike.transform.localPosition = new Vector3(
                    boxCenter.x + xSign * halfX,
                    boxCenter.y,
                    boxCenter.z + zOffsets[i]);
                spike.transform.localScale = Vector3.one;

                var mf = spike.AddComponent<MeshFilter>();
                mf.sharedMesh = coneMesh;
                var mr = spike.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

                // Visual only — strip any collider that might appear and never add one.
                var stray = spike.GetComponent<Collider>();
                if (stray != null) Destroy(stray);
            }
        }

        /// Procedural cone: a fan of side triangles from an apex at +Y*height down to a
        /// regular polygon base centred at the origin on the XZ plane, plus a base cap.
        /// Built once and shared by every spike on this ram.
        private static Mesh BuildConeMesh(float radius, float height, int sides)
        {
            sides = Mathf.Max(3, sides);

            // Vertices: [0]=apex, [1..sides]=base ring, [sides+1]=base centre.
            var verts = new Vector3[sides + 2];
            verts[0] = new Vector3(0f, height, 0f);             // apex
            int baseCenter = sides + 1;
            verts[baseCenter] = Vector3.zero;                   // base centre

            for (int i = 0; i < sides; i++)
            {
                float a = (i / (float)sides) * Mathf.PI * 2f;
                verts[1 + i] = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            }

            // Triangles: sides (apex -> ring) + base cap (centre -> ring).
            var tris = new int[sides * 3 * 2];
            int t = 0;
            for (int i = 0; i < sides; i++)
            {
                int cur = 1 + i;
                int next = 1 + ((i + 1) % sides);

                // Side face (outward winding, apex on top).
                tris[t++] = 0;
                tris[t++] = next;
                tris[t++] = cur;

                // Base cap (faces -Y / downward, winding away from the cone interior).
                tris[t++] = baseCenter;
                tris[t++] = cur;
                tris[t++] = next;
            }

            var mesh = new Mesh { name = "SlidingRamSpikeCone" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
