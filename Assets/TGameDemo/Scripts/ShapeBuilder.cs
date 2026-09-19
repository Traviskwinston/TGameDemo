using UnityEngine;

namespace GameDemo
{
    /// <summary>
    /// Tapered boxes and a few stylised primitives. Straight cubes read as programmer art;
    /// a taper is enough to get the chunky-but-shaped silhouette that stylised games use.
    /// </summary>
    public static class ShapeBuilder
    {
        /// <summary>
        /// Box whose top face can differ in size from the bottom, and can be offset sideways.
        /// Pivot sits at the bottom centre so parts stack naturally down a bone chain.
        /// </summary>
        public static Mesh TaperedBox(
            float bottomWidth, float bottomDepth,
            float topWidth, float topDepth,
            float height, Vector2 topShift = default)
        {
            return TaperedBox(bottomWidth, bottomDepth, topWidth, topDepth, height, topShift, -1, false);
        }

        /// <summary>
        /// Face indices: 0 bottom, 1 top, 2 -Z, 3 +Z, 4 -X, 5 +X. Pass one as omitFace to leave
        /// it open. insideOut flips winding and normals for a lining you see from within.
        /// </summary>
        public static Mesh TaperedBox(
            float bottomWidth, float bottomDepth,
            float topWidth, float topDepth,
            float height, Vector2 topShift, int omitFace, bool insideOut)
        {
            float bw = bottomWidth * 0.5f;
            float bd = bottomDepth * 0.5f;
            float tw = topWidth * 0.5f;
            float td = topDepth * 0.5f;
            float sx = topShift.x;
            float sz = topShift.y;

            Vector3[] c =
            {
                new Vector3(-bw, 0f, -bd),
                new Vector3(bw, 0f, -bd),
                new Vector3(bw, 0f, bd),
                new Vector3(-bw, 0f, bd),
                new Vector3(-tw + sx, height, -td + sz),
                new Vector3(tw + sx, height, -td + sz),
                new Vector3(tw + sx, height, td + sz),
                new Vector3(-tw + sx, height, td + sz)
            };

            int[][] faces =
            {
                new[] { 0, 1, 2, 3 }, // bottom (reversed below)
                new[] { 7, 6, 5, 4 }, // top
                new[] { 0, 4, 5, 1 }, // -Z
                new[] { 2, 6, 7, 3 }, // +Z
                new[] { 3, 7, 4, 0 }, // -X
                new[] { 1, 5, 6, 2 }  // +X
            };

            int faceCount = omitFace >= 0 ? 5 : 6;
            var verts = new Vector3[faceCount * 4];
            var norms = new Vector3[faceCount * 4];
            var uvs = new Vector2[faceCount * 4];
            var tris = new int[faceCount * 6];

            int written = 0;
            for (int f = 0; f < 6; f++)
            {
                if (f == omitFace)
                {
                    continue;
                }

                int[] q = faces[f];
                int vb = written * 4;
                int tb = written * 6;
                written++;

                Vector3 a = c[q[0]], b = c[q[1]], d = c[q[2]], e = c[q[3]];
                if (f == 0)
                {
                    // Flip winding so the bottom faces down.
                    verts[vb + 0] = e; verts[vb + 1] = d; verts[vb + 2] = b; verts[vb + 3] = a;
                }
                else
                {
                    verts[vb + 0] = a; verts[vb + 1] = b; verts[vb + 2] = d; verts[vb + 3] = e;
                }

                Vector3 n = Vector3.Cross(verts[vb + 1] - verts[vb + 0], verts[vb + 3] - verts[vb + 0]).normalized;
                if (insideOut)
                {
                    n = -n;
                }

                for (int i = 0; i < 4; i++)
                {
                    norms[vb + i] = n;
                }

                uvs[vb + 0] = new Vector2(0f, 0f);
                uvs[vb + 1] = new Vector2(1f, 0f);
                uvs[vb + 2] = new Vector2(1f, 1f);
                uvs[vb + 3] = new Vector2(0f, 1f);

                if (insideOut)
                {
                    tris[tb + 0] = vb + 0; tris[tb + 1] = vb + 1; tris[tb + 2] = vb + 2;
                    tris[tb + 3] = vb + 0; tris[tb + 4] = vb + 2; tris[tb + 5] = vb + 3;
                }
                else
                {
                    tris[tb + 0] = vb + 0; tris[tb + 1] = vb + 2; tris[tb + 2] = vb + 1;
                    tris[tb + 3] = vb + 0; tris[tb + 4] = vb + 3; tris[tb + 5] = vb + 2;
                }
            }

            var m = new Mesh { name = "TaperedBox" };
            m.vertices = verts;
            m.normals = norms;
            m.uv = uvs;
            m.triangles = tris;
            m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// Hood shell open at the front so the face sits inside it. Peaked and swept back.
        /// insideOut gives the dark lining you see through the opening.
        /// </summary>
        public static Mesh Hood(float width, float depth, float height, bool insideOut = false)
        {
            return TaperedBox(width, depth, width * 0.46f, depth * 0.55f, height,
                new Vector2(0f, -depth * 0.2f), 3, insideOut);
        }

        /// <summary>Box centred on its pivot, for joints and simple blocks.</summary>
        public static Mesh CenteredBox(float width, float height, float depth)
        {
            Mesh m = TaperedBox(width, depth, width, depth, height);
            Vector3[] v = m.vertices;
            for (int i = 0; i < v.Length; i++)
            {
                v[i].y -= height * 0.5f;
            }

            m.vertices = v;
            m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// Boot: pivot at the ankle, which sits at mid-height so the cuff wraps the joint.
        /// Runs forward along +Z with a short heel behind. Sole is flat; the toe end is
        /// narrower and lower so it reads as a boot, not a brick.
        /// </summary>
        public static Mesh Foot(float width, float height, float length, float heel)
        {
            // Built standing up, then laid down: the box's height axis becomes the foot length.
            // Rotation maps +Y -> +Z and +Z -> -Y, so a +Z top shift drops the toe and keeps
            // the sole flat.
            Mesh m = TaperedBox(width, height, width * 0.82f, height * 0.62f, length + heel,
                new Vector2(0f, height * 0.19f));
            Vector3[] v = m.vertices;
            Vector3[] n = m.normals;
            Quaternion q = Quaternion.Euler(90f, 0f, 0f);
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 p = q * v[i];
                p.z -= heel;
                v[i] = p;
                n[i] = q * n[i];
            }

            m.vertices = v;
            m.normals = n;
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Limb segment: slightly tapered so elbows and knees read.</summary>
        public static Mesh Limb(float topSize, float bottomSize, float length)
        {
            // Built downward: pivot at the joint, mesh hangs to -Y, so the wider end
            // must be the one that lands back at y = 0 after the shift.
            Mesh m = TaperedBox(bottomSize, bottomSize, topSize, topSize, length);
            Vector3[] v = m.vertices;
            for (int i = 0; i < v.Length; i++)
            {
                v[i].y -= length;
            }

            m.vertices = v;
            m.RecalculateBounds();
            return m;
        }
    }
}
