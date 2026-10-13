using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

/// <summary>The parallel-processing strategies shown by the gravity examples.</summary>
public enum SimulationMode
{
    SingleThreaded,
    CSharpAsync,
    CSharpParallel,
    UnityJobs,
    UnityCoroutine,
    UnityJobsBurst
}

public enum SimulationDimension
{
    TwoD,
    ThreeD
}

[StructLayout(LayoutKind.Sequential)]
public struct BodyState
{
    public Vector3 Position;
    public Vector3 Velocity;
    public float Mass;

    public BodyState(Vector3 position, Vector3 velocity, float mass)
    {
        Position = position;
        Velocity = velocity;
        Mass = mass;
    }
}

/// <summary>Shared initial state and gravity formulas used by every gravity example.</summary>
public static class GravityMath
{
    public static BodyState[] CreateInitialStates(int count, SimulationDimension dimension)
    {
        var states = new BodyState[count];
        uint random = 0xC0FFEEu;
        for (int i = 0; i < count; i++)
        {
            float y = 1f - (i / (float)Mathf.Max(1, count - 1)) * 2f;
            float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float angle = i * 2.39996323f;
            Vector3 direction = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
            if (dimension == SimulationDimension.TwoD)
                direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);

            Vector3 position = direction * (2f + NextFloat(ref random) * 4.5f);
            Vector3 tangent = dimension == SimulationDimension.TwoD
                ? new Vector3(-direction.y, direction.x, 0f)
                : Vector3.Cross(direction, Vector3.up).normalized;
            if (tangent.sqrMagnitude < 0.01f)
                tangent = Vector3.right;

            Vector3 velocity = tangent.normalized * (0.12f + NextFloat(ref random) * 0.22f);
            states[i] = new BodyState(position, velocity, 0.7f + NextFloat(ref random) * 0.8f);
        }
        return states;
    }

    public static Vector3 Acceleration(BodyState[] states, int index, float gravity, float softening, bool is2D)
    {
        Vector3 acceleration = Vector3.zero;
        Vector3 position = states[index].Position;
        float softeningSquared = softening * softening;
        for (int other = 0; other < states.Length; other++)
        {
            if (other == index)
                continue;

            Vector3 offset = states[other].Position - position;
            if (is2D)
                offset.z = 0f;
            float inverseDistance = 1f / Mathf.Sqrt(offset.sqrMagnitude + softeningSquared);
            acceleration += offset * (gravity * states[other].Mass * inverseDistance * inverseDistance * inverseDistance);
        }
        return acceleration;
    }

    /// <summary>Burst-compatible overload of the same inverse-square gravity formula.</summary>
    public static Vector3 Acceleration(NativeArray<BodyState> states, int index, float gravity,
        float softeningSquared, bool is2D)
    {
        BodyState body = states[index];
        float x = 0f;
        float y = 0f;
        float z = 0f;
        for (int other = 0; other < states.Length; other++)
        {
            if (other == index)
                continue;

            BodyState source = states[other];
            float dx = source.Position.x - body.Position.x;
            float dy = source.Position.y - body.Position.y;
            float dz = is2D ? 0f : source.Position.z - body.Position.z;
            float inverseDistance = 1f / math.sqrt(dx * dx + dy * dy + dz * dz + softeningSquared);
            float scale = gravity * source.Mass * inverseDistance * inverseDistance * inverseDistance;
            x += dx * scale;
            y += dy * scale;
            z += dz * scale;
        }
        return new Vector3(x, y, z);
    }

    public static BodyState[] Advance(BodyState[] source, float deltaTime, float gravity, float softening,
        SimulationDimension dimension)
    {
        bool is2D = dimension == SimulationDimension.TwoD;
        var accelerations = new Vector3[source.Length];
        for (int i = 0; i < source.Length; i++)
            accelerations[i] = Acceleration(source, i, gravity, softening, is2D);
        return Integrate(source, accelerations, deltaTime, is2D);
    }

    public static BodyState[] Integrate(BodyState[] source, Vector3[] accelerations, float deltaTime, bool is2D)
    {
        var result = new BodyState[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            BodyState body = source[i];
            body.Velocity += accelerations[i] * deltaTime;
            body.Position += body.Velocity * deltaTime;
            if (is2D)
            {
                body.Velocity.z = 0f;
                body.Position.z = 0f;
            }
            result[i] = body;
        }
        return result;
    }

    public static BodyState[] Integrate(BodyState[] source, NativeArray<Vector3> accelerations,
        float deltaTime, bool is2D)
    {
        var result = new BodyState[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            BodyState body = source[i];
            body.Velocity += accelerations[i] * deltaTime;
            body.Position += body.Velocity * deltaTime;
            if (is2D)
            {
                body.Velocity.z = 0f;
                body.Position.z = 0f;
            }
            result[i] = body;
        }
        return result;
    }

    private static float NextFloat(ref uint state)
    {
        state = state * 1664525u + 1013904223u;
        return (state & 0x00FFFFFFu) / 16777216f;
    }
}
