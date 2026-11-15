#if PRODUCTION_AVAILABLE
// MmdMotion uses the production name normalizer. Its map-building dependencies
// are irrelevant to parsing and deliberately fail if a test accidentally calls
// them instead of exercising the production UpstreamAvatar mapping.
namespace SFM.Actions;
public static class MotionRig
{
    public static string MmdSource(string path,ISet<string> paths)=>throw new NotSupportedException("No preset fallback in the upstream oracle");
    public static bool IsSupportedPath(string path)=>throw new NotSupportedException("No preset fallback in the upstream oracle");
}
#endif
