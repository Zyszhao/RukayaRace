using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RukayaAviation;

// Bounded-curvature approach. All six Dubins families finish at the commanded start
// with the commanded heading; turns never consume the attack segment itself.
public static class AttackApproach
{
    private static double Mod(double a)
    {
        double value = (a % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI);
        // A negative float rounding error at zero must not turn into a full orbit.
        return value < 0.00001 || 2 * Math.PI - value < 0.00001 ? 0 : value;
    }
    public static float Length(Vector3 from, IEnumerable<Vector3> path)
    {
        float length = 0;
        foreach (Vector3 p in path) { length += AV.Distance(from, p); from = p; }
        return length;
    }
    // Capture a target strip already just ahead of the nose. The normal upstream
    // staging point may now be behind us: returning to it would require an orbit.
    // Keep the ground targets intact and join a parallel firing track after a
    // small, bounded-radius heading correction instead.
    public static List<Vector3> CaptureForward(Vector3 from, float yaw, Vector3 first, Vector3 last,
        Map map, float lead, float noseClearance, out Vector3 entry, out Vector3 offset)
    {
        entry = from; offset = Vector3.zero;
        Vector3 forward = (last - first).normalized;
        float delta = Mathf.DeltaAngle(yaw, forward.AngleFlat());
        float ahead = Vector3.Dot(first - from, forward);
        if (Mathf.Abs(delta) > 30 || ahead < noseClearance || ahead > lead + 8) return null;
        var path = new List<Vector3>();
        float radians = delta * Mathf.Deg2Rad, sign = Mathf.Sign(radians), heading = yaw * Mathf.Deg2Rad;
        int steps = Math.Max(1, Mathf.CeilToInt(Mathf.Abs(radians) * 10 / 0.5f));
        for (int i = 1; i <= steps; i++)
        {
            float next = heading + radians * i / steps;
            Vector3 p = from;
            if (Mathf.Abs(radians) > 0.00001f)
            {
                p.x += 10 / sign * (Mathf.Cos(heading) - Mathf.Cos(next));
                p.z += 10 / sign * (Mathf.Sin(next) - Mathf.Sin(heading));
            }
            if (!AV.SafeAir(p.ToIntVec3(), map, 12)) return null;
            path.Add(p);
        }
        entry = path[^1];
        offset = entry - first - forward * Vector3.Dot(entry - first, forward);
        if (offset.magnitude > 2.5f || Vector3.Dot(first - entry, forward) < noseClearance) return null;
        return path;
    }
    public static List<Vector3> PlanMove(Vector3 from, float yaw, Vector3 to, Map map)
    {
        List<Vector3> best = null; float length = float.MaxValue;
        float bearing = (to - from).AngleFlat();
        foreach (float endYaw in new[] { bearing, yaw, bearing - 45, bearing + 45, bearing - 90, bearing + 90, bearing + 180 })
        {
            List<Vector3> candidate = Plan(from, yaw, to, endYaw, map);
            if (candidate == null) continue;
            float distance = Length(from, candidate);
            if (distance < length) { best = candidate; length = distance; }
            if (length <= AV.Distance(from, to) + 0.01f) break;
        }
        return best;
    }
    public static List<Vector3> PlanEntry(Vector3 from, float yaw, Vector3 to, float finalYaw, Map map)
    {
        List<Vector3> best = null; float bestLength = float.MaxValue;
        foreach (float extension in new[] { 0f, 4, 8, 12, 18, 24, 32 })
        {
            Vector3 join = to - AV.Dir(finalYaw) * extension;
            if (!AV.SafeAir(join.ToIntVec3(), map, 12)) continue;
            List<Vector3> path = Plan(from, yaw, join, finalYaw, map);
            if (path == null) continue;
            if (extension > 0) path.Add(to);
            float length = Length(from, path);
            if (length < bestLength) { bestLength = length; best = path; }
            if (bestLength <= AV.Distance(from, to) + 0.01f) break;
        }
        return best;
    }
    public static List<Vector3> Plan(Vector3 from, float yaw, Vector3 to, float finalYaw, Map map, float radius = 10f)
    {
        if (AV.Distance(from, to) < 0.001f && Mathf.Abs(Mathf.DeltaAngle(yaw, finalYaw)) < 0.01f) return new() { to };
        float directYaw = (to - from).AngleFlat();
        if (Mathf.Abs(Mathf.DeltaAngle(yaw, directYaw)) < 0.005f && Mathf.Abs(Mathf.DeltaAngle(finalYaw, directYaw)) < 0.005f
            && AV.SafeAir(from.ToIntVec3(), map, 12) && AV.SafeAir(to.ToIntVec3(), map, 12)) return new() { to };
        double theta = Math.Atan2(to.z - from.z, to.x - from.x);
        double a = Mod(Math.PI / 2 - yaw * Math.PI / 180 - theta);
        double b = Mod(Math.PI / 2 - finalYaw * Math.PI / 180 - theta);
        double d = AV.Distance(from, to) / radius, sa = Math.Sin(a), sb = Math.Sin(b), ca = Math.Cos(a), cb = Math.Cos(b), cab = Math.Cos(a - b);
        var choices = new List<(string mode, double t, double p, double q)>();
        void Add(string mode, double t, double p, double q) { if (!double.IsNaN(t + p + q)) choices.Add((mode, Mod(t), p, Mod(q))); }
        double p2 = 2 + d * d - 2 * cab + 2 * d * (sa - sb);
        if (p2 >= 0) { double x = Math.Atan2(cb - ca, d + sa - sb); Add("LSL", -a + x, Math.Sqrt(p2), b - x); }
        p2 = 2 + d * d - 2 * cab + 2 * d * (sb - sa);
        if (p2 >= 0) { double x = Math.Atan2(ca - cb, d - sa + sb); Add("RSR", a - x, Math.Sqrt(p2), -b + x); }
        p2 = -2 + d * d + 2 * cab + 2 * d * (sa + sb);
        if (p2 >= 0) { double p = Math.Sqrt(p2), x = Math.Atan2(-ca - cb, d + sa + sb) - Math.Atan2(-2, p); Add("LSR", -a + x, p, -b + x); }
        p2 = d * d - 2 + 2 * cab - 2 * d * (sa + sb);
        if (p2 >= 0) { double p = Math.Sqrt(p2), x = Math.Atan2(ca + cb, d - sa - sb) - Math.Atan2(2, p); Add("RSL", a - x, p, b - x); }
        double v = (6 - d * d + 2 * cab + 2 * d * (sa - sb)) / 8;
        if (Math.Abs(v) <= 1) { double p = Mod(2 * Math.PI - Math.Acos(v)), t = Mod(a - Math.Atan2(ca - cb, d - sa + sb) + p / 2); Add("RLR", t, p, a - b - t + p); }
        v = (6 - d * d + 2 * cab + 2 * d * (-sa + sb)) / 8;
        if (Math.Abs(v) <= 1) { double p = Mod(2 * Math.PI - Math.Acos(v)), t = Mod(-a - Math.Atan2(ca - cb, d + sa - sb) + p / 2); Add("LRL", t, p, b - a - t + p); }
        foreach (var c in choices.OrderBy(c => c.t + c.p + c.q))
        {
            var path = new List<Vector3>(); Vector3 pos = from;
            double heading = Math.PI / 2 - yaw * Math.PI / 180;
            double[] lengths = { c.t, c.p, c.q };
            for (int j = 0; j < 3; j++)
            {
                double remaining = lengths[j] * radius;
                while (remaining > 0.00001)
                {
                    double ds = Math.Min(0.5, remaining); remaining -= ds;
                    if (c.mode[j] == 'S') { pos.x += (float)(Math.Cos(heading) * ds); pos.z += (float)(Math.Sin(heading) * ds); }
                    else
                    {
                        double sign = c.mode[j] == 'L' ? 1 : -1, next = heading + sign * ds / radius;
                        pos.x += (float)(radius / sign * (Math.Sin(next) - Math.Sin(heading)));
                        pos.z += (float)(radius / sign * (-Math.Cos(next) + Math.Cos(heading))); heading = next;
                    }
                    path.Add(pos);
                }
            }
            if (AV.Distance(pos, to) > 0.05f || path.Any(p => !AV.SafeAir(p.ToIntVec3(), map, 12))) continue;
            if (path.Count == 0) path.Add(to); else path[^1] = to;
            return path;
        }
        return null;
    }
}
