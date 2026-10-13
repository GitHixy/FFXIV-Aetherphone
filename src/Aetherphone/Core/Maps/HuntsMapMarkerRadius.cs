using Aetherphone.Core.Hunts;

namespace Aetherphone.Core.Maps;

internal static class HuntsMapMarkerRadius
{
    public const int CandidateMinimum = 35;
    public const int CandidateMaximum = 200;
    public const int TrackedSpot = 90;
    public const int Fate = 35;
    private const float NearestNeighbourShare = 0.4f;

    public static int CandidateFor(ReadOnlySpan<HuntsMapMarkerPoint> points, int sizeFactor)
    {
        if (points.Length < 2)
        {
            return CandidateMaximum;
        }

        var nearestSquared = float.MaxValue;
        for (var index = 0; index < points.Length; index++)
        {
            for (var otherIndex = index + 1; otherIndex < points.Length; otherIndex++)
            {
                var deltaX = points[index].RawX - points[otherIndex].RawX;
                var deltaY = points[index].RawY - points[otherIndex].RawY;
                var distanceSquared = deltaX * deltaX + deltaY * deltaY;
                if (distanceSquared < nearestSquared)
                {
                    nearestSquared = distanceSquared;
                }
            }
        }

        var pixelsPerWorldUnit = Math.Max(sizeFactor, 1) / 100f;
        var nearestWorldDistance = MathF.Sqrt(nearestSquared) / pixelsPerWorldUnit;
        var radius = (int)MathF.Round(nearestWorldDistance * NearestNeighbourShare);
        return Math.Clamp(radius, CandidateMinimum, CandidateMaximum);
    }

    public static int ScaleFor(HuntsMapMarkerState state, int candidateScale) => state switch
    {
        HuntsMapMarkerState.Candidate => candidateScale,
        HuntsMapMarkerState.FateInactive => Fate,
        HuntsMapMarkerState.FateActive => Fate,
        _ => TrackedSpot,
    };
}
