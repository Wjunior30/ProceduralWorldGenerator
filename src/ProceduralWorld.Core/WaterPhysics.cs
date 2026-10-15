using System.Numerics;

namespace ProceduralWorld.Core;

public readonly record struct WaterContact(bool InWater, bool Swimming, float Submersion, float Surface, float Depth, Vector2 Current);

/// <summary>SimulationLife's validated wading, drag and buoyancy model. No renderer or engine dependencies.</summary>
public static class WaterPhysics
{
    public const float BodyHeight = 1.80f, FeetOffset = .90f, SwimSubmersion = .56f;
    public const float CurrentSpeed = 3.20f, BuoyancyRatio = 1.38f, VerticalDrag = 3.2f;
    public const float DryResponse = 24f, WetResponse = 8f, SwimSpeedRatio = .42f;

    /// <param name="current">Horizontal current normalized to CurrentSpeed (3.2 metres per second).</param>
    public static WaterContact Sample(float bodyY, float surfaceY, float depth, Vector2 current)
    {
        bool valid = float.IsFinite(surfaceY) && depth > .025f;
        float submersion = valid ? Clamp((surfaceY - (bodyY - FeetOffset)) / BodyHeight, 0, 1) : 0;
        return new(valid && submersion > .001f, valid && submersion >= SwimSubmersion, submersion,
            surfaceY, Math.Max(0, depth), valid ? current : Vector2.Zero);
    }

    public static bool IsSwimming(WaterContact contact, bool previouslySwimming)
        => contact.InWater && contact.Submersion >= (previouslySwimming ? .50f : SwimSubmersion);

    public static Vector2 HorizontalVelocity(Vector2 velocity, Vector2 input, Vector2 current, float submersion, float delta)
    {
        ValidateDelta(delta);
        float t = Clamp(submersion, 0, 1), wet = t * t * (3 - 2 * t);
        Vector2 target = input * Lerp(1, SwimSpeedRatio, wet) + current * CurrentSpeed * MathF.Sqrt(t);
        Vector2 difference = target - velocity;
        float distance = difference.Length(), step = Lerp(DryResponse, WetResponse, wet) * delta;
        return distance <= step || distance < .00001f ? target : velocity + difference / distance * step;
    }

    public static float VerticalVelocity(float velocity, float submersion, float gravity, bool swimUp, float delta)
    {
        ValidateDelta(delta);
        float wet = Clamp(submersion, 0, 1);
        float result = velocity + (-gravity + gravity * BuoyancyRatio * wet) * delta;
        result = MoveToward(result, 0, Math.Abs(result) * VerticalDrag * wet * delta);
        if (swimUp && wet >= SwimSubmersion) result = MoveToward(result, 3.4f, 9f * delta);
        return Clamp(result, -12f, 4.5f);
    }

    private static void ValidateDelta(float delta)
    {
        if (!float.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
    }
    private static float Clamp(float x, float min, float max) => Math.Max(min, Math.Min(max, x));
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    private static float MoveToward(float from, float to, float step)
        => Math.Abs(to - from) <= step ? to : from + Math.Sign(to - from) * step;
}
